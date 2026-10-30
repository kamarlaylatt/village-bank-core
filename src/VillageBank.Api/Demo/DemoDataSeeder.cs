using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Api.Demo;

public record DemoSeedResult(bool Seeded, int Members, int Loans);

// Fills an empty database with six months of believable history so a demo has something to show.
// It drives the real services (not raw SQL) against a simulated clock, so the ledger stays balanced
// and every business rule applies exactly as it does in production. It only runs when asked, through
// the seed-demo command, and does nothing if the database already has members.
public class DemoDataSeeder
{
    private const int HistoryDays = 180;
    private const int RandomSeed = 20261030;
    private const decimal ShareCapital = 150_000_000m;
    private const string ShareCapitalCode = "3000";

    private enum Ending
    {
        RepaidOnTime,
        PaidOnTime,
        EarlyPayoff,
        PartialLate,
        Unpaid,
        Rejected,
        Pending,
        ApprovedOnly
    }

    // StartDay counts from the first day of the history. Count means "installments paid on time"
    // for PaidOnTime and "the day of the payoff" for EarlyPayoff.
    private sealed record LoanScenario(
        int Member, int StartDay, decimal Principal, decimal Rate, int Term, string Purpose, Ending Ending, int Count = 0);

    // Chosen so that, with the history ending today, every aging bucket has a loan in it.
    // The comments give roughly how many days late the oldest unpaid installment is.
    private static readonly LoanScenario[] Scenarios =
    {
        new(0, 10, 2_000_000m, 2m, 4, "Rice farming inputs", Ending.RepaidOnTime),
        new(1, 40, 3_000_000m, 2m, 6, "Buy livestock", Ending.EarlyPayoff, Count: 100),
        new(2, 110, 1_500_000m, 1.5m, 6, "Small shop stock", Ending.PaidOnTime, Count: 2),
        new(3, 165, 2_500_000m, 2m, 6, "Weaving materials", Ending.Unpaid),
        new(4, 135, 4_000_000m, 2m, 6, "Fish pond", Ending.Unpaid),
        new(5, 105, 1_000_000m, 2.5m, 6, "Motorbike repair", Ending.Unpaid),
        new(6, 75, 5_000_000m, 2m, 6, "Coffee farm tools", Ending.Unpaid),
        new(7, 30, 6_000_000m, 2m, 6, "Vegetable garden expansion", Ending.Unpaid),
        new(8, 100, 3_000_000m, 2m, 6, "School fees", Ending.PartialLate),
        new(9, 150, 2_000_000m, 2m, 6, "Tailoring machine", Ending.Rejected),
        new(10, 178, 2_500_000m, 2m, 6, "Kitchen equipment", Ending.Pending),
        new(11, 170, 1_800_000m, 2m, 6, "Fertiliser", Ending.ApprovedOnly)
    };

    private sealed record Toolkit(
        BankDbContext Db,
        ILedgerService Ledger,
        ISavingsService Savings,
        ILoanService Loans,
        IMemberService Members);

    private sealed record SeedEvent(int Day, int Order, long UserId, UserRole Role, Func<Toolkit, Task> Run);

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly TimeProvider _realClock;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IServiceScopeFactory scopes,
        IConfiguration config,
        TimeProvider realClock,
        ILogger<DemoDataSeeder> logger)
    {
        _scopes = scopes;
        _config = config;
        _realClock = realClock;
        _logger = logger;
    }

    public async Task<DemoSeedResult> SeedAsync(CancellationToken ct)
    {
        // Checked first, so a weak password stops the seeder before anything is written.
        var password = _config["Seed:Demo:Password"];
        PasswordPolicy.Validate(password, "teller01");

        using (var scope = _scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();

            if (await db.Members.AnyAsync(ct))
            {
                _logger.LogInformation("Demo data skipped: the database already has members.");
                return new DemoSeedResult(false, 0, 0);
            }
        }

        var staff = await EnsureStaffAsync(password!, ct);
        var manager = staff[UserRole.Manager];
        var teller = staff[UserRole.Teller];

        var today = BusinessTime.Today(_realClock);
        var start = today.AddDays(-HistoryDays);

        var memberCount = DemoPeople.Names.Length;
        var memberIds = new long[memberCount];
        var savingsIds = new long[memberCount];
        var loanIds = new long[Scenarios.Length];

        var rng = new Random(RandomSeed);
        var events = new List<SeedEvent>();

        void Add(int day, long userId, UserRole role, Func<Toolkit, Task> run) =>
            events.Add(new SeedEvent(day, events.Count, userId, role, run));

        // Day 0: the founding members pay in the share capital.
        Add(0, manager, UserRole.Manager, async x =>
        {
            var ids = await x.Db.Accounts
                .Where(a => a.Code == AccountCodes.Cash || a.Code == ShareCapitalCode)
                .ToDictionaryAsync(a => a.Code, a => a.Id, ct);

            await x.Ledger.PostAsync(
                start,
                "Share capital contributed by founding members",
                new[]
                {
                    new LineInput(ids[AccountCodes.Cash], ShareCapital, 0m),
                    new LineInput(ids[ShareCapitalCode], 0m, ShareCapital)
                },
                ct: ct);
        });

        // One new member every other day, each opening with a small deposit.
        for (var i = 0; i < memberCount; i++)
        {
            var index = i;
            var openingDeposit = Round1000(rng.Next(50_000, 300_000));

            Add(CreatedDay(index), teller, UserRole.Teller, async x =>
            {
                var member = await x.Members.CreateAsync(
                    new CreateMemberRequest(DemoPeople.Names[index], DemoPeople.Phone(index), DemoPeople.Town(index)), ct);

                memberIds[index] = member.Id;
                savingsIds[index] = member.SavingsAccountId;
            });

            Add(CreatedDay(index), teller, UserRole.Teller, x =>
                x.Savings.DepositAsync(savingsIds[index], openingDeposit, "Opening deposit", null, ct));
        }

        // Once a month, about half the members save a little and a few take some out.
        for (var cycle = 1; cycle <= HistoryDays / 30; cycle++)
        {
            var day = cycle * 30;

            for (var i = 0; i < memberCount; i++)
            {
                // Every value is drawn for every member, so the sequence does not depend on who has joined.
                var depositRoll = rng.Next(100);
                var depositAmount = Round1000(rng.Next(20_000, 200_000));
                var withdrawRoll = rng.Next(100);
                var withdrawFraction = 0.1m + rng.Next(30) / 100m;

                if (CreatedDay(i) >= day)
                    continue;

                var index = i;

                if (depositRoll < 55)
                {
                    Add(day, teller, UserRole.Teller, x =>
                        x.Savings.DepositAsync(savingsIds[index], depositAmount, "Monthly savings", null, ct));
                }

                if (withdrawRoll < 12)
                {
                    Add(day, teller, UserRole.Teller, async x =>
                    {
                        var account = await x.Savings.GetAccountAsync(savingsIds[index], ct);
                        var amount = Round1000(account.Balance * withdrawFraction);

                        if (amount >= 1_000m && amount <= account.Balance)
                            await x.Savings.WithdrawAsync(savingsIds[index], amount, "Cash withdrawal", null, ct);
                    });
                }
            }
        }

        for (var k = 0; k < Scenarios.Length; k++)
        {
            var scenario = Scenarios[k];
            var loanIndex = k;
            var memberIndex = scenario.Member;

            Add(scenario.StartDay, teller, UserRole.Teller, async x =>
            {
                var loan = await x.Loans.ApplyAsync(
                    new ApplyLoanRequest(
                        memberIds[memberIndex], scenario.Principal, scenario.Rate, scenario.Term, scenario.Purpose),
                    ct);

                loanIds[loanIndex] = loan.Id;
            });

            if (scenario.Ending == Ending.Pending)
                continue;

            if (scenario.Ending == Ending.Rejected)
            {
                Add(scenario.StartDay + 1, manager, UserRole.Manager, x =>
                    x.Loans.RejectAsync(loanIds[loanIndex], "Insufficient savings history", ct));
                continue;
            }

            if (scenario.Ending == Ending.ApprovedOnly)
            {
                Add(scenario.StartDay + 2, manager, UserRole.Manager, x => x.Loans.ApproveAsync(loanIds[loanIndex], ct));
                continue;
            }

            Add(scenario.StartDay, manager, UserRole.Manager, x => x.Loans.ApproveAsync(loanIds[loanIndex], ct));
            Add(scenario.StartDay, teller, UserRole.Teller, x => x.Loans.DisburseAsync(loanIds[loanIndex], ct));

            // The loan lands in the member's savings, and the member takes it out in cash.
            Add(scenario.StartDay, teller, UserRole.Teller, x =>
                x.Savings.WithdrawAsync(savingsIds[memberIndex], scenario.Principal, "Loan proceeds paid out in cash", null, ct));

            var schedule = LoanScheduleCalculator.Calculate(
                scenario.Principal, scenario.Rate, scenario.Term, start.AddDays(scenario.StartDay));

            void Pay(int day, decimal amount)
            {
                if (day > HistoryDays || amount <= 0)
                    return;

                Add(day, teller, UserRole.Teller, x =>
                    x.Loans.RepayAsync(loanIds[loanIndex], amount, "Installment payment", null, ct));
            }

            int DayOf(DateOnly date) => date.DayNumber - start.DayNumber;

            switch (scenario.Ending)
            {
                case Ending.RepaidOnTime:
                    foreach (var installment in schedule)
                        Pay(DayOf(installment.DueDate), installment.Principal + installment.Interest);
                    break;

                case Ending.PaidOnTime:
                    foreach (var installment in schedule.Take(scenario.Count))
                        Pay(DayOf(installment.DueDate), installment.Principal + installment.Interest);
                    break;

                case Ending.EarlyPayoff:
                    Pay(scenario.Count, schedule.Sum(i => i.Principal + i.Interest));
                    break;

                case Ending.PartialLate:
                {
                    var first = schedule[0];
                    Pay(DayOf(first.DueDate) + 5, Math.Round((first.Principal + first.Interest) * 0.6m));
                    break;
                }
            }
        }

        var clock = new SimulatedClock(Noon(start));
        var currentDay = -1;
        var step = 0;

        foreach (var seedEvent in events.OrderBy(e => e.Day).ThenBy(e => e.Order))
        {
            ct.ThrowIfCancellationRequested();

            if (seedEvent.Day != currentDay)
            {
                currentDay = seedEvent.Day;
                step = 0;
            }

            // Events of one day are spread over a few minutes so their timestamps keep their order.
            clock.Set(Noon(start.AddDays(seedEvent.Day)).AddSeconds(step++ * 20));

            await RunAsync(seedEvent, clock);
        }

        _logger.LogInformation(
            "Demo data seeded: {Members} members and {Loans} loans over {Days} days.",
            memberCount, Scenarios.Length, HistoryDays);

        return new DemoSeedResult(true, memberCount, Scenarios.Length);
    }

    private async Task RunAsync(SeedEvent seedEvent, SimulatedClock clock)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();

        var user = new FixedCurrentUser(seedEvent.UserId, seedEvent.Role);
        var ledger = new LedgerService(db, user);
        var savings = new SavingsService(db, ledger, clock, user);
        var loans = new LoanService(db, ledger, savings, clock, user);
        var members = new MemberService(db, clock);

        await seedEvent.Run(new Toolkit(db, ledger, savings, loans, members));
    }

    private async Task<Dictionary<UserRole, long>> EnsureStaffAsync(string password, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        var ids = new Dictionary<UserRole, long>();

        foreach (var (username, role) in new[]
                 {
                     ("manager01", UserRole.Manager),
                     ("teller01", UserRole.Teller),
                     ("auditor01", UserRole.Auditor)
                 })
        {
            var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);

            if (user is null)
            {
                user = new User
                {
                    Username = username,
                    Role = role,
                    IsActive = true,
                    MustChangePassword = false,
                    CreatedAt = _realClock.GetUtcNow().UtcDateTime
                };
                user.PasswordHash = hasher.HashPassword(user, password);

                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            ids[role] = user.Id;
        }

        return ids;
    }

    // Member i joins on day 1 + 2i.
    private static int CreatedDay(int memberIndex) => 1 + 2 * memberIndex;

    // Noon in Vientiane (UTC+7) is 05:00 UTC, which keeps every event on its intended business date.
    private static DateTimeOffset Noon(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(5, 0)), TimeSpan.Zero);

    private static decimal Round1000(decimal value) => Math.Round(value / 1000m, 0) * 1000m;
}
