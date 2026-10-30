namespace VillageBank.Api.Demo;

// A clock the demo seeder moves by hand. Every service takes a TimeProvider, so running them against
// this clock produces records dated months ago, including installments that are genuinely overdue.
public sealed class SimulatedClock : TimeProvider
{
    private DateTimeOffset _now;

    public SimulatedClock(DateTimeOffset start)
    {
        _now = start;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Set(DateTimeOffset utcNow) => _now = utcNow;
}
