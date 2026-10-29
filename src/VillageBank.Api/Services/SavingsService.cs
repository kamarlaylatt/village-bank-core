using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class SavingsService : ISavingsService
{
    private const decimal MaxAmount = 1_000_000_000_000m;

    private readonly BankDbContext _db;
    private readonly ILedgerService _ledger;
    private readonly TimeProvider _clock;

    public SavingsService(BankDbContext db, ILedgerService ledger, TimeProvider clock)
    {
        _db = db;
        _ledger = ledger;
        _clock = clock;
    }

    public async Task<SavingsAccountSummary> GetAccountAsync(long accountId, CancellationToken ct = default)
    {
        return await _db.SavingsAccounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new SavingsAccountSummary(a.Id, a.MemberId, a.Member.FullName, a.Balance, a.IsActive))
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Savings account {accountId} was not found.");
    }

    public Task<SavingsTransactionResult> DepositAsync(
        long accountId, decimal amount, string? note, string? idempotencyKey, CancellationToken ct = default) =>
        ApplyAsync(accountId, SavingsTransactionType.Deposit, amount, note, idempotencyKey, ct);

    public Task<SavingsTransactionResult> WithdrawAsync(
        long accountId, decimal amount, string? note, string? idempotencyKey, CancellationToken ct = default) =>
        ApplyAsync(accountId, SavingsTransactionType.Withdrawal, amount, note, idempotencyKey, ct);

    public async Task<IReadOnlyList<StatementLine>> GetStatementAsync(
        long accountId, int take, CancellationToken ct = default)
    {
        if (!await _db.SavingsAccounts.AnyAsync(a => a.Id == accountId, ct))
            throw new NotFoundException($"Savings account {accountId} was not found.");

        var rows = await _db.SavingsTransactions
            .AsNoTracking()
            .Where(t => t.SavingsAccountId == accountId)
            .OrderByDescending(t => t.Id)
            .Take(Math.Clamp(take, 1, 200))
            .Select(t => new { t.Id, t.Type, t.Amount, t.BalanceAfter, t.Note, t.CreatedAt })
            .ToListAsync(ct);

        return rows
            .Select(t => new StatementLine(t.Id, t.Type.ToString(), t.Amount, t.BalanceAfter, t.Note, t.CreatedAt))
            .ToList();
    }

    private async Task<SavingsTransactionResult> ApplyAsync(
        long accountId,
        SavingsTransactionType type,
        decimal amount,
        string? note,
        string? idempotencyKey,
        CancellationToken ct)
    {
        ValidateAmount(amount);
        if (idempotencyKey is { Length: > 100 })
            throw new BusinessRuleException("Idempotency key can be at most 100 characters.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Row lock: a concurrent request for the same account waits here until we commit,
        // then reads the updated balance. This is what stops two withdrawals overdrawing.
        var account = (await _db.SavingsAccounts
                .FromSqlInterpolated($"SELECT * FROM savings_accounts WHERE id = {accountId} FOR UPDATE")
                .ToListAsync(ct))
            .SingleOrDefault()
            ?? throw new NotFoundException($"Savings account {accountId} was not found.");

        if (!account.IsActive)
            throw new BusinessRuleException("Savings account is not active.");

        // Checked after the lock so two retries of the same request cannot both pass.
        if (idempotencyKey is not null)
        {
            var existing = await _db.SavingsTransactions
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.SavingsAccountId == accountId && t.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null)
            {
                if (existing.Type != type || existing.Amount != amount)
                    throw new BusinessRuleException("Idempotency key was already used for a different request.");

                return ToResult(existing, replayed: true);
            }
        }

        if (type == SavingsTransactionType.Withdrawal && account.Balance < amount)
            throw new BusinessRuleException(
                $"Insufficient funds: balance {account.Balance}, requested {amount}.");

        account.Balance += type == SavingsTransactionType.Deposit ? amount : -amount;

        var ledgerAccounts = await GetLedgerAccountIdsAsync(ct);
        var cash = ledgerAccounts[AccountCodes.Cash];
        var memberSavings = ledgerAccounts[AccountCodes.MemberSavings];

        // Deposit: Dr Cash / Cr Member Savings. Withdrawal is the reverse.
        var lines = type == SavingsTransactionType.Deposit
            ? new[] { new LineInput(cash, amount, 0m), new LineInput(memberSavings, 0m, amount) }
            : new[] { new LineInput(memberSavings, amount, 0m), new LineInput(cash, 0m, amount) };

        var entry = await _ledger.StageAsync(
            BusinessTime.Today(_clock),
            $"Savings {type.ToString().ToLowerInvariant()} - account {accountId}",
            lines,
            ct: ct);

        var transaction = new SavingsTransaction
        {
            SavingsAccountId = accountId,
            Type = type,
            Amount = amount,
            BalanceAfter = account.Balance,
            IdempotencyKey = idempotencyKey,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            JournalEntry = entry,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };

        _db.SavingsTransactions.Add(transaction);

        // One SaveChanges writes the balance, the savings transaction and the journal entry together.
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToResult(transaction, replayed: false);
    }

    private async Task<Dictionary<string, int>> GetLedgerAccountIdsAsync(CancellationToken ct)
    {
        var codes = new[] { AccountCodes.Cash, AccountCodes.MemberSavings };

        var ids = await _db.Accounts
            .Where(a => codes.Contains(a.Code))
            .ToDictionaryAsync(a => a.Code, a => a.Id, ct);

        if (ids.Count != codes.Length)
            throw new LedgerException("Required ledger accounts are missing from the chart of accounts.");

        return ids;
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Amount must be greater than zero.");
        if (amount > MaxAmount)
            throw new BusinessRuleException("Amount is too large.");
        if (decimal.Round(amount, 4) != amount)
            throw new BusinessRuleException("Amount can have at most 4 decimal places.");
    }

    private static SavingsTransactionResult ToResult(SavingsTransaction t, bool replayed) =>
        new(t.Id, t.SavingsAccountId, t.Type.ToString(), t.Amount, t.BalanceAfter, t.JournalEntryId, t.CreatedAt, replayed);
}
