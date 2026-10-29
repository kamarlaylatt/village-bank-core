namespace VillageBank.Api.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "VillageBank";
    public string Audience { get; set; } = "VillageBank.Api";
    public string SigningKey { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 60;
}

public static class JwtClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Role = "role";
}
