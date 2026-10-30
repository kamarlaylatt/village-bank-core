namespace VillageBank.Api.Domain;

public static class BusinessTime
{
    private static readonly TimeZoneInfo Zone =
        ResolveZone(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null);

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    // Linux and macOS know the IANA id. Windows names the same zone "SE Asia Standard Time" (UTC+7), and only
    // knows the IANA id when ICU data is installed. Laos has no daylight saving, so if neither id is found
    // a fixed UTC+7 offset gives exactly the same answer instead of stopping the app on a Windows host.
    internal static TimeZoneInfo ResolveZone(Func<string, TimeZoneInfo?> find)
    {
        foreach (var id in new[] { "Asia/Vientiane", "SE Asia Standard Time" })
        {
            var zone = find(id);
            if (zone is not null)
                return zone;
        }

        return TimeZoneInfo.CreateCustomTimeZone("Asia/Vientiane", TimeSpan.FromHours(7), "Vientiane", "Vientiane");
    }
}
