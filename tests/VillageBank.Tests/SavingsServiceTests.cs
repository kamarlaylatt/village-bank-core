using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Tests;

[Collection("postgres")]
public class SavingsServiceTests
{
    private readonly PostgresFixture _fixture;

    public SavingsServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Deposit_increases_balance_and_posts_a_balanced_ledger_entry()
    {
        var accountId = await CreateAccountAsync();

        await using var db = _fixture.CreateDbContext();
        var result = await CreateSavings(db).DepositAsync(accountId, 50_000m, "first deposit", null);

        Assert.Equal(50_000m, result.BalanceAfter);
        Assert.False(result.Replayed);

        await using var check = _fixture.CreateDbContext();

        var account = await check.SavingsAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        Assert.Equal(50_000m, account.Balance);

        var lines = await check.JournalLines
            .AsNoTracking()
            .Include(l => l.Account)
            .Where(l => l.JournalEntryId == result.JournalEntryId)
            .ToListAsync();

        Assert.Equal(2, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Equal(50_000m, lines.Single(l => l.Account.Code == AccountCodes.Cash).Debit);
        Assert.Equal(50_000m, lines.Single(l => l.Account.Code == AccountCodes.MemberSavings).Credit);
    }

    [Fact]
    public async Task Withdrawal_reverses_the_ledger_sides()
    {
        var accountId = await CreateAccountAsync(initialDeposit: 10_000m);

        await using var db = _fixture.CreateDbContext();
        var result = await CreateSavings(db).WithdrawAsync(accountId, 4_000m, null, null);

        Assert.Equal(6_000m, result.BalanceAfter);

        await using var check = _fixture.CreateDbContext();
        var lines = await check.JournalLines
            .AsNoTracking()
            .Include(l => l.Account)
            .Where(l => l.JournalEntryId == result.JournalEntryId)
            .ToListAsync();

        Assert.Equal(4_000m, lines.Single(l => l.Account.Code == AccountCodes.MemberSavings).Debit);
        Assert.Equal(4_000m, lines.Single(l => l.Account.Code == AccountCodes.Cash).Credit);
    }

    [Fact]
    public async Task Withdrawing_more_than_the_balance_is_rejected_and_changes_nothing()
    {
        var accountId = await CreateAccountAsync(initialDeposit: 1_000m);

        await using var db = _fixture.CreateDbContext();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateSavings(db).WithdrawAsync(accountId, 2_000m, null, null));

        Assert.Contains("Insufficient funds", ex.Message);

        await using var check = _fixture.CreateDbContext();
        var account = await check.SavingsAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        Assert.Equal(1_000m, account.Balance);
        Assert.Equal(1, await check.SavingsTransactions.CountAsync(t => t.SavingsAccountId == accountId));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(0.00001)]
    public async Task Invalid_amounts_are_rejected(double amount)
    {
        var accountId = await CreateAccountAsync();

        await using var db = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateSavings(db).DepositAsync(accountId, (decimal)amount, null, null));
    }

    [Fact]
    public async Task Unknown_account_throws_not_found()
    {
        await using var db = _fixture.CreateDbContext();
        await Assert.ThrowsAsync<NotFoundException>(
            () => CreateSavings(db).DepositAsync(long.MaxValue, 100m, null, null));
    }

    [Fact]
    public async Task Repeating_a_request_with_the_same_idempotency_key_does_not_post_twice()
    {
        var accountId = await CreateAccountAsync();

        await using var db1 = _fixture.CreateDbContext();
        var first = await CreateSavings(db1).DepositAsync(accountId, 50_000m, null, "key-1");

        await using var db2 = _fixture.CreateDbContext();
        var second = await CreateSavings(db2).DepositAsync(accountId, 50_000m, null, "key-1");

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.TransactionId, second.TransactionId);

        await using var check = _fixture.CreateDbContext();
        var account = await check.SavingsAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        Assert.Equal(50_000m, account.Balance);
        Assert.Equal(1, await check.SavingsTransactions.CountAsync(t => t.SavingsAccountId == accountId));
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_a_different_amount_is_rejected()
    {
        var accountId = await CreateAccountAsync();

        await using var db1 = _fixture.CreateDbContext();
        await CreateSavings(db1).DepositAsync(accountId, 50_000m, null, "key-2");

        await using var db2 = _fixture.CreateDbContext();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => CreateSavings(db2).DepositAsync(accountId, 60_000m, null, "key-2"));

        Assert.Contains("different request", ex.Message);
    }

    [Fact]
    public async Task Concurrent_withdrawals_never_overdraw_the_account()
    {
        var accountId = await CreateAccountAsync(initialDeposit: 1_000m);

        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var db = _fixture.CreateDbContext();
            try
            {
                await CreateSavings(db).WithdrawAsync(accountId, 100m, null, null);
                return true;
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(10, results.Count(succeeded => succeeded));

        await using var check = _fixture.CreateDbContext();
        var account = await check.SavingsAccounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        Assert.Equal(0m, account.Balance);

        var withdrawals = await check.SavingsTransactions.CountAsync(
            t => t.SavingsAccountId == accountId && t.Type == SavingsTransactionType.Withdrawal);
        Assert.Equal(10, withdrawals);
    }

    [Fact]
    public async Task Statement_lists_newest_first_and_balance_matches_ledger()
    {
        var accountId = await CreateAccountAsync();

        await using var db = _fixture.CreateDbContext();
        var savings = CreateSavings(db);
        await savings.DepositAsync(accountId, 3_000m, "a", null);
        await savings.DepositAsync(accountId, 2_000m, "b", null);
        await savings.WithdrawAsync(accountId, 1_000m, "c", null);

        var statement = await savings.GetStatementAsync(accountId, take: 10);

        Assert.Equal(new[] { "c", "b", "a" }, statement.Select(s => s.Note));
        Assert.Equal(4_000m, statement[0].BalanceAfter);

        await using var check = _fixture.CreateDbContext();
        Assert.Equal(
            await check.JournalLines.SumAsync(l => l.Debit),
            await check.JournalLines.SumAsync(l => l.Credit));
    }

    private async Task<long> CreateAccountAsync(decimal initialDeposit = 0m)
    {
        await using var db = _fixture.CreateDbContext();

        var member = await new MemberService(db, TimeProvider.System)
            .CreateAsync(new CreateMemberRequest($"Test Member {Guid.NewGuid():N}", null, null));

        if (initialDeposit > 0)
            await CreateSavings(db).DepositAsync(member.SavingsAccountId, initialDeposit, null, null);

        return member.SavingsAccountId;
    }

    private static SavingsService CreateSavings(BankDbContext db) =>
        new(db, new LedgerService(db, TestCurrentUser.Anonymous), TimeProvider.System, TestCurrentUser.Anonymous);
}
