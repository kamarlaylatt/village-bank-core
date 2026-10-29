using Microsoft.EntityFrameworkCore;
using Npgsql;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class LoanService : ILoanService
{
    private const string InProgressMessage = "The member already has a loan in progress.";
    private const decimal MaxRepayment = 1_000_000_000_000m;

    private readonly BankDbContext _db;
    private readonly ILedgerService _ledger;
    private readonly ISavingsService _savings;
    private readonly TimeProvider _clock;
    private readonly ICurrentUser _currentUser;

    public LoanService(
        BankDbContext db, ILedgerService ledger, ISavingsService savings, TimeProvider clock, ICurrentUser currentUser)
    {
        _db = db;
        _ledger = ledger;
        _savings = savings;
        _clock = clock;
        _currentUser = currentUser;
    }

    public LoanPreview Preview(LoanPreviewRequest request)
    {
        var schedule = LoanScheduleCalculator.Calculate(
            request.Principal, request.MonthlyInterestRatePercent, request.TermMonths, BusinessTime.Today(_clock));

        var totalInterest = schedule.Sum(s => s.Interest);

        return new LoanPreview(
            request.Principal,
            totalInterest,
            request.Principal + totalInterest,
            schedule
                .Select(s => new InstallmentPreview(s.Number, s.DueDate, s.Principal, s.Interest, s.Principal + s.Interest))
                .ToList());
    }

    public async Task<LoanDetails> ApplyAsync(ApplyLoanRequest request, CancellationToken ct = default)
    {
        LoanRules.ValidateTerms(request.Principal, request.MonthlyInterestRatePercent, request.TermMonths);

        var purpose = string.IsNullOrWhiteSpace(request.Purpose) ? null : request.Purpose.Trim();
        if (purpose is { Length: > 200 })
            throw new BusinessRuleException("Purpose can be at most 200 characters.");

        var member = await _db.Members
            .AsNoTracking()
            .Where(m => m.Id == request.MemberId)
            .Select(m => new { m.IsActive })
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Member {request.MemberId} was not found.");

        if (!member.IsActive)
            throw new BusinessRuleException("The member is not active.");

        var inProgress = await _db.Loans.AnyAsync(
            l => l.MemberId == request.MemberId && l.Status != LoanStatus.Rejected && l.Status != LoanStatus.Closed, ct);
        if (inProgress)
            throw new BusinessRuleException(InProgressMessage);

        var loan = new Loan
        {
            MemberId = request.MemberId,
            Principal = request.Principal,
            MonthlyInterestRatePercent = request.MonthlyInterestRatePercent,
            TermMonths = request.TermMonths,
            Purpose = purpose,
            Status = LoanStatus.Pending,
            AppliedAt = Now,
            AppliedByUserId = _currentUser.UserId
        };

        _db.Loans.Add(loan);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two applications raced past the check above; the partial unique index caught the second.
            throw new BusinessRuleException(InProgressMessage);
        }

        return await GetAsync(loan.Id, ct);
    }

    public async Task<LoanDetails> GetAsync(long id, CancellationToken ct = default)
    {
        var loan = await _db.Loans
            .AsNoTracking()
            .Include(l => l.Member)
            .Include(l => l.Installments)
            .SingleOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException($"Loan {id} was not found.");

        // The loan stores only user ids; the UI shows who acted, so resolve them to usernames.
        var userIds = new[] { loan.AppliedByUserId, loan.DecidedByUserId, loan.DisbursedByUserId }
            .Where(userId => userId.HasValue)
            .Select(userId => userId!.Value)
            .Distinct()
            .ToList();

        var userNames = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        return ToDetails(loan, BusinessTime.Today(_clock), userNames);
    }

    public async Task<PagedResult<LoanSummary>> ListAsync(
        long? memberId, LoanStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Loans.AsNoTracking();

        if (memberId is { } mid)
            query = query.Where(l => l.MemberId == mid);
        if (status is { } st)
            query = query.Where(l => l.Status == st);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new LoanSummary(
                l.Id, l.MemberId, l.Member.FullName, l.Principal, l.Status, l.AppliedAt, l.DisbursedOn))
            .ToListAsync(ct);

        return new PagedResult<LoanSummary>(items, page, pageSize, total);
    }

    public async Task<LoanDetails> ApproveAsync(long id, CancellationToken ct = default)
    {
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var loan = await LockLoanAsync(id, ct);
            RequireStatus(loan, LoanStatus.Pending, "approved");

            // Maker-checker: the person who entered the application cannot also approve it.
            if (_currentUser.UserId is not null && loan.AppliedByUserId == _currentUser.UserId)
                throw new BusinessRuleException("A loan cannot be approved by the person who applied for it.");

            loan.Status = LoanStatus.Approved;
            loan.DecidedAt = Now;
            loan.DecidedByUserId = _currentUser.UserId;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return await GetAsync(id, ct);
    }

    public async Task<LoanDetails> RejectAsync(long id, string reason, CancellationToken ct = default)
    {
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason is null)
            throw new BusinessRuleException("A reason is required to reject a loan.");
        if (cleanReason.Length > 500)
            throw new BusinessRuleException("Reason can be at most 500 characters.");

        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var loan = await LockLoanAsync(id, ct);
            RequireStatus(loan, LoanStatus.Pending, "rejected");

            loan.Status = LoanStatus.Rejected;
            loan.RejectionReason = cleanReason;
            loan.DecidedAt = Now;
            loan.DecidedByUserId = _currentUser.UserId;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return await GetAsync(id, ct);
    }

    public async Task<LoanDetails> DisburseAsync(long id, CancellationToken ct = default)
    {
        await using (var tx = await _db.Database.BeginTransactionAsync(ct))
        {
            var loan = await LockLoanAsync(id, ct);
            RequireStatus(loan, LoanStatus.Approved, "disbursed");

            var today = BusinessTime.Today(_clock);

            // The schedule starts on the disbursement date, so it is created now rather than at application.
            var schedule = LoanScheduleCalculator.Calculate(
                loan.Principal, loan.MonthlyInterestRatePercent, loan.TermMonths, today);

            // Dr Loans Receivable / Cr Member Savings: the principal goes straight into the member's
            // savings account. It is only staged, so it commits together with the loan in one SaveChanges.
            var proceeds = await _savings.StageLoanProceedsAsync(
                loan.MemberId, loan.Principal, today, $"Loan disbursement - loan {id}", ct);

            loan.Status = LoanStatus.Active;
            loan.DisbursedOn = today;
            loan.DisbursedByUserId = _currentUser.UserId;
            loan.DisbursementJournalEntry = proceeds.JournalEntry;

            foreach (var item in schedule)
            {
                loan.Installments.Add(new LoanInstallment
                {
                    Number = item.Number,
                    DueDate = item.DueDate,
                    PrincipalDue = item.Principal,
                    InterestDue = item.Interest
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        return await GetAsync(id, ct);
    }

    public async Task<RepaymentResult> RepayAsync(
        long id, decimal amount, string? note, string? idempotencyKey, CancellationToken ct = default)
    {
        ValidateRepaymentAmount(amount);
        if (idempotencyKey is { Length: > 100 })
            throw new BusinessRuleException("Idempotency key can be at most 100 characters.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var loan = await LockLoanAsync(id, ct);

        // A replay is answered before the status check, so retrying the repayment that
        // closed the loan still returns that repayment instead of an error.
        if (idempotencyKey is not null)
        {
            var existing = await _db.LoanRepayments
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.LoanId == id && r.IdempotencyKey == idempotencyKey, ct);

            if (existing is not null)
            {
                if (existing.Amount != amount)
                    throw new BusinessRuleException("Idempotency key was already used for a different request.");

                return ToResult(existing, loan.Status, await OutstandingAsync(id, ct), replayed: true);
            }
        }

        if (loan.Status != LoanStatus.Active)
            throw new BusinessRuleException($"Only active loans can be repaid (current status: {loan.Status}).");

        var installments = await _db.LoanInstallments
            .Where(i => i.LoanId == id)
            .OrderBy(i => i.Number)
            .ToListAsync(ct);

        var outstanding = installments.Sum(i => i.TotalDue - i.TotalPaid);
        if (amount > outstanding)
            throw new BusinessRuleException($"Amount exceeds the outstanding balance of {outstanding}.");

        var (principalPart, interestPart) = Allocate(installments, amount);

        var accounts = await GetLedgerAccountIdsAsync(
            ct, AccountCodes.Cash, AccountCodes.LoansReceivable, AccountCodes.InterestIncome);

        // Dr Cash for everything received; Cr Loans Receivable for the principal, Cr Interest Income
        // for the interest. A line with a zero amount is not allowed, so unused parts are skipped.
        var lines = new List<LineInput> { new(accounts[AccountCodes.Cash], amount, 0m) };
        if (principalPart > 0)
            lines.Add(new LineInput(accounts[AccountCodes.LoansReceivable], 0m, principalPart));
        if (interestPart > 0)
            lines.Add(new LineInput(accounts[AccountCodes.InterestIncome], 0m, interestPart));

        var today = BusinessTime.Today(_clock);
        var entry = await _ledger.StageAsync(today, $"Loan repayment - loan {id}", lines, ct: ct);

        var repayment = new LoanRepayment
        {
            LoanId = id,
            Amount = amount,
            PrincipalPart = principalPart,
            InterestPart = interestPart,
            PaidOn = today,
            IdempotencyKey = idempotencyKey,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            JournalEntry = entry,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = Now
        };

        _db.LoanRepayments.Add(repayment);

        var outstandingAfter = outstanding - amount;
        if (outstandingAfter == 0)
        {
            loan.Status = LoanStatus.Closed;
            loan.ClosedAt = Now;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return ToResult(repayment, loan.Status, outstandingAfter, replayed: false);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    // Row lock: concurrent changes to the same loan wait here until the first one commits.
    private async Task<Loan> LockLoanAsync(long id, CancellationToken ct) =>
        (await _db.Loans
            .FromSqlInterpolated($"SELECT * FROM loans WHERE id = {id} FOR UPDATE")
            .ToListAsync(ct))
        .SingleOrDefault()
        ?? throw new NotFoundException($"Loan {id} was not found.");

    private static void RequireStatus(Loan loan, LoanStatus expected, string action)
    {
        if (loan.Status != expected)
            throw new BusinessRuleException(
                $"Only {expected.ToString().ToLowerInvariant()} loans can be {action} (current status: {loan.Status}).");
    }

    // Oldest installment first; inside an installment the interest is paid before the principal.
    private static (decimal Principal, decimal Interest) Allocate(IEnumerable<LoanInstallment> installments, decimal amount)
    {
        var remaining = amount;
        var principalPaid = 0m;
        var interestPaid = 0m;

        foreach (var installment in installments)
        {
            if (remaining == 0)
                break;

            var interest = Math.Min(remaining, installment.InterestDue - installment.InterestPaid);
            installment.InterestPaid += interest;
            remaining -= interest;
            interestPaid += interest;

            var principal = Math.Min(remaining, installment.PrincipalDue - installment.PrincipalPaid);
            installment.PrincipalPaid += principal;
            remaining -= principal;
            principalPaid += principal;
        }

        return (principalPaid, interestPaid);
    }

    private async Task<Dictionary<string, int>> GetLedgerAccountIdsAsync(CancellationToken ct, params string[] codes)
    {
        var ids = await _db.Accounts
            .Where(a => codes.Contains(a.Code))
            .ToDictionaryAsync(a => a.Code, a => a.Id, ct);

        if (ids.Count != codes.Length)
            throw new LedgerException("Required ledger accounts are missing from the chart of accounts.");

        return ids;
    }

    private Task<decimal> OutstandingAsync(long loanId, CancellationToken ct) =>
        _db.LoanInstallments
            .Where(i => i.LoanId == loanId)
            .SumAsync(i => i.PrincipalDue + i.InterestDue - i.PrincipalPaid - i.InterestPaid, ct);

    private static void ValidateRepaymentAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Amount must be greater than zero.");
        if (amount > MaxRepayment)
            throw new BusinessRuleException("Amount is too large.");
        if (amount != decimal.Truncate(amount))
            throw new BusinessRuleException("Amount must be a whole number of kip.");
    }

    private static RepaymentResult ToResult(LoanRepayment r, LoanStatus status, decimal outstanding, bool replayed) =>
        new(r.Id, r.LoanId, r.Amount, r.PrincipalPart, r.InterestPart, outstanding, status, r.JournalEntryId, r.CreatedAt, replayed);

    private static LoanDetails ToDetails(Loan loan, DateOnly today, IReadOnlyDictionary<long, string> userNames)
    {
        string? NameOf(long? userId) =>
            userId is { } uid && userNames.TryGetValue(uid, out var name) ? name : null;

        var installments = loan.Installments.OrderBy(i => i.Number).ToList();

        var totalInterest = installments.Count > 0
            ? installments.Sum(i => i.InterestDue)
            : LoanScheduleCalculator.TotalInterest(loan.Principal, loan.MonthlyInterestRatePercent, loan.TermMonths);

        var overdue = installments.Where(i => i.DueDate < today && !i.IsPaid).ToList();
        var daysPastDue = overdue.Count == 0 ? 0 : today.DayNumber - overdue.Min(i => i.DueDate).DayNumber;

        return new LoanDetails(
            loan.Id,
            loan.MemberId,
            loan.Member.FullName,
            loan.Principal,
            loan.MonthlyInterestRatePercent,
            loan.TermMonths,
            loan.Purpose,
            loan.Status,
            loan.AppliedAt,
            loan.DecidedAt,
            loan.RejectionReason,
            loan.DisbursedOn,
            totalInterest,
            installments.Sum(i => i.TotalPaid),
            installments.Sum(i => i.TotalDue - i.TotalPaid),
            overdue.Sum(i => i.TotalDue - i.TotalPaid),
            daysPastDue,
            installments
                .Select(i => new InstallmentDetails(
                    i.Number, i.DueDate, i.PrincipalDue, i.InterestDue, i.PrincipalPaid, i.InterestPaid, i.IsPaid))
                .ToList(),
            NameOf(loan.AppliedByUserId),
            NameOf(loan.DecidedByUserId),
            NameOf(loan.DisbursedByUserId));
    }
}
