using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Tests;

// Every test here works in its own empty database, because report totals add up the whole ledger
// and would otherwise include data written by the other tests.
[Collection("postgres")]
public class ReportServiceTests
{
    // Account ids seeded by the model (HasData).
    private const int CashAccountId = 1;
    private const int MemberSavingsAccountId = 3;
    private const int ShareCapitalAccountId = 4;
    private const string ShareCapitalCode = "3000";

    private static readonly DateOnly Today = BusinessTime.Today(TimeProvider.System);

    private readonly PostgresFixture _fixture;

    public ReportServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // The village story, in kip:
    //   1. members pay in 1,000,000 of share capital
    //   2. Ma Aye saves 50,000
    //   3. she borrows 300,000 over 10 months at 2% a month; the loan is paid into her savings
    //   4. she withdraws the 300,000 in cash
    //   5. she repays 106,000, which is 18,000 interest (3 installments) and 88,000 principal
    // Left in the books: cash 856,000, loans receivable 212,000, member savings 50,000,
    // share capital 1,000,000 and interest income 18,000.
    private async Task<string> VillageStoryAsync()
    {
        var cs = await _fixture.CreateIsolatedDatabaseAsync();

        await using (var db = Db(cs))
        {
            await new LedgerService(db, TestCurrentUser.Anonymous).PostAsync(
                Today,
                "Share capital",
                new[]
                {
                    new LineInput(CashAccountId, 1_000_000m, 0m),
                    new LineInput(ShareCapitalAccountId, 0m, 1_000_000m)
                });
        }

        var (memberId, savingsAccountId) = await CreateMemberAsync(cs, "Ma Aye");
        await AsSavings(cs, s => s.DepositAsync(savingsAccountId, 50_000m, null, null));

        var teller = await CreateUserAsync(cs, UserRole.Teller);
        var manager = await CreateUserAsync(cs, UserRole.Manager);
        var loanId = await ApplyApproveDisburseAsync(cs, memberId, teller, manager);

        await AsSavings(cs, s => s.WithdrawAsync(savingsAccountId, 300_000m, null, null));
        await AsLoans(cs, null, s => s.RepayAsync(loanId, 106_000m, null, null));

        return cs;
    }

    [Fact]
    public async Task The_trial_balance_matches_the_village_story_and_balances()
    {
        var cs = await VillageStoryAsync();

        var report = await AsReports(cs, s => s.TrialBalanceAsync(Today));

        Assert.True(report.IsBalanced);
        Assert.Equal(report.TotalDebit, report.TotalCredit);
        Assert.Equal(1_068_000m, report.TotalDebit);

        Assert.Equal(856_000m, Line(report, AccountCodes.Cash).Debit);
        Assert.Equal(212_000m, Line(report, AccountCodes.LoansReceivable).Debit);
        Assert.Equal(50_000m, Line(report, AccountCodes.MemberSavings).Credit);
        Assert.Equal(1_000_000m, Line(report, ShareCapitalCode).Credit);
        Assert.Equal(18_000m, Line(report, AccountCodes.InterestIncome).Credit);
    }

    [Fact]
    public async Task The_trial_balance_only_counts_entries_up_to_the_as_of_date()
    {
        var cs = await VillageStoryAsync();

        var report = await AsReports(cs, s => s.TrialBalanceAsync(Today.AddDays(-1)));

        Assert.True(report.IsBalanced);
        Assert.Equal(0m, report.TotalDebit);
        Assert.All(report.Lines, l => Assert.Equal(0m, l.Debit + l.Credit));
    }

    [Fact]
    public async Task The_balance_sheet_balances_with_earnings_counted_as_equity()
    {
        var cs = await VillageStoryAsync();

        var report = await AsReports(cs, s => s.BalanceSheetAsync(Today));

        Assert.Equal(1_068_000m, report.TotalAssets);
        Assert.Equal(50_000m, report.TotalLiabilities);
        Assert.Equal(18_000m, report.EarningsToDate);
        Assert.Equal(1_018_000m, report.TotalEquity);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public async Task The_income_statement_reports_interest_income_for_the_period()
    {
        var cs = await VillageStoryAsync();

        var report = await AsReports(cs, s => s.IncomeStatementAsync(Today, Today));

        Assert.Equal(18_000m, report.TotalIncome);
        Assert.Equal(0m, report.TotalExpenses);
        Assert.Equal(18_000m, report.NetProfit);

        var yesterday = await AsReports(cs, s => s.IncomeStatementAsync(Today.AddDays(-1), Today.AddDays(-1)));
        Assert.Equal(0m, yesterday.NetProfit);
    }

    [Fact]
    public async Task The_cash_book_lists_cash_movements_with_a_running_balance()
    {
        var cs = await VillageStoryAsync();

        var book = await AsReports(cs, s => s.CashBookAsync(Today));

        Assert.Equal(0m, book.OpeningBalance);
        Assert.Equal(new[] { 1_000_000m, 50_000m, 0m, 106_000m }, book.Lines.Select(l => l.Receipt));
        Assert.Equal(new[] { 0m, 0m, 300_000m, 0m }, book.Lines.Select(l => l.Payment));
        Assert.Equal(new[] { 1_000_000m, 1_050_000m, 750_000m, 856_000m }, book.Lines.Select(l => l.Balance));
        Assert.Equal(1_156_000m, book.TotalReceipts);
        Assert.Equal(300_000m, book.TotalPayments);
        Assert.Equal(856_000m, book.ClosingBalance);
    }

    [Fact]
    public async Task The_cash_book_of_a_later_day_opens_with_the_previous_closing_balance()
    {
        var cs = await VillageStoryAsync();

        var book = await AsReports(cs, s => s.CashBookAsync(Today.AddDays(1)));

        Assert.Equal(856_000m, book.OpeningBalance);
        Assert.Empty(book.Lines);
        Assert.Equal(856_000m, book.ClosingBalance);
    }

    [Fact]
    public async Task Reconciliation_passes_when_the_books_agree()
    {
        var cs = await VillageStoryAsync();

        var report = await AsReports(cs, s => s.ReconciliationAsync());

        Assert.True(report.AllOk);
        Assert.Equal(4, report.Checks.Count);
        Assert.All(report.Checks, c => Assert.Equal(0m, c.Difference));
    }

    [Fact]
    public async Task Reconciliation_flags_a_manual_entry_that_has_no_matching_savings_transaction()
    {
        var cs = await VillageStoryAsync();

        // A manager posts straight to the ledger, bypassing the savings accounts.
        await using (var db = Db(cs))
        {
            await new LedgerService(db, TestCurrentUser.Anonymous).PostAsync(
                Today,
                "Manual correction",
                new[]
                {
                    new LineInput(CashAccountId, 1_000m, 0m),
                    new LineInput(MemberSavingsAccountId, 0m, 1_000m)
                });
        }

        var report = await AsReports(cs, s => s.ReconciliationAsync());

        var savings = report.Checks.Single(c => c.Name == "Member savings");
        Assert.False(savings.IsOk);
        Assert.Equal(1_000m, savings.Difference);
        Assert.False(report.AllOk);

        // The entry itself is balanced, so the ledger-level checks still pass.
        Assert.True(report.Checks.Single(c => c.Name == "Ledger totals").IsOk);
    }

    [Fact]
    public async Task Loan_aging_groups_loans_by_days_past_due_and_calculates_par()
    {
        var cs = await _fixture.CreateIsolatedDatabaseAsync();
        var teller = await CreateUserAsync(cs, UserRole.Teller);
        var manager = await CreateUserAsync(cs, UserRole.Manager);

        var (memberA, _) = await CreateMemberAsync(cs, "Member A");
        var (memberB, _) = await CreateMemberAsync(cs, "Member B");
        var loanA = await ApplyApproveDisburseAsync(cs, memberA, teller, manager);
        var loanB = await ApplyApproveDisburseAsync(cs, memberB, teller, manager);

        // Member B's first installment fell due 45 days ago and is still unpaid.
        await using (var db = Db(cs))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE loan_installments SET due_date = {Today.AddDays(-45)} WHERE loan_id = {loanB} AND number = 1");
        }

        var report = await AsReports(cs, s => s.LoanAgingAsync());

        Assert.Equal(2, report.ActiveLoans);
        Assert.Equal(600_000m, report.TotalPrincipalOutstanding);

        Assert.Equal(new[] { "Current", "1-30", "31-60", "61-90", "90+" }, report.Buckets.Select(b => b.Label));
        Assert.Equal(new[] { 1, 0, 1, 0, 0 }, report.Buckets.Select(b => b.Loans));
        Assert.Equal(36_000m, report.Buckets.Single(b => b.Label == "31-60").OverdueAmount);

        Assert.Equal(50m, report.PortfolioAtRisk.Single(p => p.Name == "PAR1").Percent);
        Assert.Equal(50m, report.PortfolioAtRisk.Single(p => p.Name == "PAR30").Percent);
        Assert.Equal(0m, report.PortfolioAtRisk.Single(p => p.Name == "PAR60").Percent);

        var overdue = Assert.Single(report.OverdueLoans);
        Assert.Equal(loanB, overdue.LoanId);
        Assert.Equal(45, overdue.DaysPastDue);
        Assert.Equal(36_000m, overdue.OverdueAmount);
        Assert.NotEqual(loanA, overdue.LoanId);
    }

    [Fact]
    public async Task Loan_aging_ignores_loans_that_are_not_active()
    {
        var cs = await _fixture.CreateIsolatedDatabaseAsync();
        var teller = await CreateUserAsync(cs, UserRole.Teller);
        var (memberId, _) = await CreateMemberAsync(cs, "Pending Member");

        await AsLoans(cs, teller, s => s.ApplyAsync(new ApplyLoanRequest(memberId, 300_000m, 2m, 10, null)));

        var report = await AsReports(cs, s => s.LoanAgingAsync());

        Assert.Equal(0, report.ActiveLoans);
        Assert.Equal(0m, report.TotalPrincipalOutstanding);
        Assert.All(report.PortfolioAtRisk, p => Assert.Equal(0m, p.Percent));
    }

    [Theory]
    [InlineData(0, "Current")]
    [InlineData(1, "1-30")]
    [InlineData(30, "1-30")]
    [InlineData(31, "31-60")]
    [InlineData(90, "61-90")]
    [InlineData(91, "90+")]
    [InlineData(400, "90+")]
    public void Days_past_due_map_to_the_expected_bucket(int days, string expected)
    {
        Assert.Equal(expected, ReportService.BucketLabel(days));
    }

    private static TrialBalanceLine Line(TrialBalanceReport report, string code) =>
        report.Lines.Single(l => l.Code == code);

    private BankDbContext Db(string connectionString) => _fixture.CreateDbContext(connectionString);

    private async Task<T> AsReports<T>(string cs, Func<ReportService, Task<T>> action)
    {
        await using var db = Db(cs);
        return await action(new ReportService(db, TimeProvider.System));
    }

    private async Task<T> AsSavings<T>(string cs, Func<SavingsService, Task<T>> action)
    {
        await using var db = Db(cs);
        var user = TestCurrentUser.Anonymous;

        return await action(new SavingsService(db, new LedgerService(db, user), TimeProvider.System, user));
    }

    private async Task<T> AsLoans<T>(string cs, long? userId, Func<LoanService, Task<T>> action)
    {
        await using var db = Db(cs);
        var user = new TestCurrentUser { UserId = userId };
        var ledger = new LedgerService(db, user);
        var savings = new SavingsService(db, ledger, TimeProvider.System, user);

        return await action(new LoanService(db, ledger, savings, TimeProvider.System, user));
    }

    private async Task<(long MemberId, long SavingsAccountId)> CreateMemberAsync(string cs, string name)
    {
        await using var db = Db(cs);

        var member = await new MemberService(db, TimeProvider.System)
            .CreateAsync(new CreateMemberRequest(name, null, null));

        return (member.Id, member.SavingsAccountId);
    }

    // Loans record who acted, which is a foreign key to users, so the tests need real user rows.
    private async Task<long> CreateUserAsync(string cs, UserRole role)
    {
        await using var db = Db(cs);

        var user = new User
        {
            Username = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20],
            PasswordHash = "not-a-real-hash",
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<long> ApplyApproveDisburseAsync(string cs, long memberId, long teller, long manager)
    {
        var loan = await AsLoans(cs, teller, s => s.ApplyAsync(new ApplyLoanRequest(memberId, 300_000m, 2m, 10, "test")));
        await AsLoans(cs, manager, s => s.ApproveAsync(loan.Id));
        await AsLoans(cs, teller, s => s.DisburseAsync(loan.Id));

        return loan.Id;
    }
}
