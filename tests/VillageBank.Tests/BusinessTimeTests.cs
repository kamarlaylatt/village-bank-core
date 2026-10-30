using VillageBank.Api.Domain;

namespace VillageBank.Tests;

public class BusinessTimeTests
{
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedClock(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    [Theory]
    [InlineData("2026-10-30T00:00:00Z", "2026-10-30")] // 07:00 in Vientiane
    [InlineData("2026-10-30T16:59:00Z", "2026-10-30")] // 23:59 in Vientiane
    [InlineData("2026-10-30T17:00:00Z", "2026-10-31")] // midnight in Vientiane
    public void The_business_day_follows_Vientiane_time_not_UTC(string utc, string expected)
    {
        var clock = new FixedClock(DateTimeOffset.Parse(utc));

        Assert.Equal(DateOnly.Parse(expected), BusinessTime.Today(clock));
    }

    [Fact]
    public void The_IANA_zone_id_is_used_when_the_system_has_it()
    {
        var iana = TimeZoneInfo.CreateCustomTimeZone("iana", TimeSpan.FromHours(7), "iana", "iana");

        var zone = BusinessTime.ResolveZone(id => id == "Asia/Vientiane" ? iana : null);

        Assert.Same(iana, zone);
    }

    [Fact]
    public void The_Windows_zone_id_is_used_when_the_IANA_id_is_unknown()
    {
        var windows = TimeZoneInfo.CreateCustomTimeZone("windows", TimeSpan.FromHours(7), "windows", "windows");

        var zone = BusinessTime.ResolveZone(id => id == "SE Asia Standard Time" ? windows : null);

        Assert.Same(windows, zone);
    }

    [Fact]
    public void A_fixed_UTC_plus_7_zone_is_the_last_resort_so_the_app_never_fails_to_start()
    {
        var zone = BusinessTime.ResolveZone(_ => null);

        Assert.Equal(TimeSpan.FromHours(7), zone.BaseUtcOffset);
        Assert.False(zone.SupportsDaylightSavingTime);
    }
}
