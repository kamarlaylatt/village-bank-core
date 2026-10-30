using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VillageBank.Api.Data;
using VillageBank.Api.Demo;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Tests;

// Seeding takes a few seconds, so the demo database is built once and the tests below only read it.
[Collection("postgres")]
public class DemoDataSeederTests
{
    private const string DemoPassword = "Demo-Password-1";
    private const int HistoryDays = 180;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string? _seededConnectionString;
    private static DemoSeedResult? _seedResult;

    private static readonly DateOnly Today = BusinessTime.Today(TimeProvider.System);

    private readonly PostgresFixture _fixture;

    public DemoDataSeederTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Seeding_creates_members_staff_and_loans_in_every_state()
    {
        var (cs, result) = await SeededAsync();

        Assert.True(result.Seeded);
        Assert.Equal(30, result.Members);
        Assert.Equal(12, result.Loans);

        await using var db = _fixture.CreateDbContext(cs);
        Assert.Equal(30, await db.Members.CountAsync());

        var byStatus = (await db.Loans
                .GroupBy(l => l.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(g => g.Status, g => g.Count);

        Assert.Equal(7, byStatus[LoanStatus.Active]);
        Assert.Equal(2, byStatus[LoanStatus.Closed]);
        Assert.Equal(1, byStatus[LoanStatus.Rejected]);
        Assert.Equal(1, byStatus[LoanStatus.Pending]);
        Assert.Equal(1, byStatus[LoanStatus.Approved]);

        var roles = await db.Users.Select(u => u.Role).ToListAsync();
        Assert.Contains(UserRole.Manager, roles);
        Assert.Contains(UserRole.Teller, roles);
        Assert.Contains(UserRole.Auditor, roles);
    }

    [Fact]
    public async Task The_demo_books_balance_and_reconcile()
    {
        var (cs, _) = await SeededAsync();

        await using var db = _fixture.CreateDbContext(cs);
        var reports = new ReportService(db, TimeProvider.System);

        var trialBalance = await reports.TrialBalanceAsync(Today);
        Assert.True(trialBalance.IsBalanced);
        Assert.True(trialBalance.Lines.Single(l => l.Code == AccountCodes.Cash).Debit > 0, "cash should not be overdrawn");

        Assert.True((await reports.BalanceSheetAsync(Today)).IsBalanced);

        var reconciliation = await reports.ReconciliationAsync();
        Assert.True(reconciliation.AllOk, string.Join("; ", reconciliation.Checks.Where(c => !c.IsOk).Select(c => c.Name)));
    }

    [Fact]
    public async Task The_demo_portfolio_has_loans_in_every_aging_bucket()
    {
        var (cs, _) = await SeededAsync();

        await using var db = _fixture.CreateDbContext(cs);
        var report = await new ReportService(db, TimeProvider.System).LoanAgingAsync();

        Assert.Equal(7, report.ActiveLoans);
        Assert.Equal(new[] { "Current", "1-30", "31-60", "61-90", "90+" }, report.Buckets.Select(b => b.Label));
        Assert.Equal(new[] { 2, 1, 2, 1, 1 }, report.Buckets.Select(b => b.Loans));

        Assert.True(report.PortfolioAtRisk.Single(p => p.Name == "PAR30").Percent > 0);
        Assert.True(report.PortfolioAtRisk.Single(p => p.Name == "PAR90").Percent > 0);
    }

    [Fact]
    public async Task The_history_covers_about_six_months()
    {
        var (cs, _) = await SeededAsync();

        await using var db = _fixture.CreateDbContext(cs);

        Assert.Equal(Today.AddDays(-HistoryDays), await db.JournalEntries.MinAsync(e => e.EntryDate));
        Assert.True(await db.JournalEntries.MaxAsync(e => e.EntryDate) <= Today);
        Assert.Equal(Today.AddDays(-HistoryDays + 1), await db.Members.MinAsync(m => m.JoinedOn));
    }

    [Fact]
    public async Task Everything_is_attributed_to_a_staff_member()
    {
        var (cs, _) = await SeededAsync();

        await using var db = _fixture.CreateDbContext(cs);

        Assert.True(await db.JournalEntries.AllAsync(e => e.CreatedByUserId != null));
        Assert.True(await db.Loans.AllAsync(l => l.AppliedByUserId != null));
    }

    [Fact]
    public async Task The_demo_staff_can_sign_in_with_the_configured_password()
    {
        var (cs, _) = await SeededAsync();

        await using var db = _fixture.CreateDbContext(cs);
        var hasher = new PasswordHasher<User>();

        foreach (var username in new[] { "manager01", "teller01", "auditor01" })
        {
            var user = await db.Users.SingleAsync(u => u.Username == username);

            Assert.False(user.MustChangePassword);
            Assert.NotEqual(
                PasswordVerificationResult.Failed,
                hasher.VerifyHashedPassword(user, user.PasswordHash, DemoPassword));
        }
    }

    [Fact]
    public async Task Seeding_again_does_nothing()
    {
        var (cs, _) = await SeededAsync();

        var second = await CreateSeeder(cs, DemoPassword).SeedAsync(default);

        Assert.False(second.Seeded);

        await using var db = _fixture.CreateDbContext(cs);
        Assert.Equal(30, await db.Members.CountAsync());
        Assert.Equal(12, await db.Loans.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    public async Task A_missing_or_weak_password_stops_the_seeder_before_anything_is_written(string? password)
    {
        var cs = await _fixture.CreateIsolatedDatabaseAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => CreateSeeder(cs, password).SeedAsync(default));

        await using var db = _fixture.CreateDbContext(cs);
        Assert.Equal(0, await db.Members.CountAsync());
        Assert.Equal(0, await db.Users.CountAsync());
    }

    private async Task<(string ConnectionString, DemoSeedResult Result)> SeededAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_seededConnectionString is null)
            {
                var cs = await _fixture.CreateIsolatedDatabaseAsync();
                _seedResult = await CreateSeeder(cs, DemoPassword).SeedAsync(default);
                _seededConnectionString = cs;
            }

            return (_seededConnectionString, _seedResult!);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static DemoDataSeeder CreateSeeder(string connectionString, string? password)
    {
        var services = new ServiceCollection();
        services.AddDbContext<BankDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        var provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:Demo:Password"] = password })
            .Build();

        return new DemoDataSeeder(
            provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            TimeProvider.System,
            NullLogger<DemoDataSeeder>.Instance);
    }
}
