using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using VillageBank.Api.Data;

namespace VillageBank.Api.Auth;

public class ConfigureJwtBearerOptions : IConfigureNamedOptions<JwtBearerOptions>
{
    private readonly JwtOptions _jwt;

    public ConfigureJwtBearerOptions(IOptions<JwtOptions> jwt)
    {
        _jwt = jwt.Value;
    }

    public void Configure(JwtBearerOptions options) =>
        Configure(JwtBearerDefaults.AuthenticationScheme, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme)
            return;

        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = _jwt.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtClaims.Name,
            RoleClaimType = JwtClaims.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidateAgainstDatabaseAsync
        };
    }

    // A valid signature is not enough: the account must still exist, be active and hold the
    // same role as when the token was issued. This is what makes deactivation take effect at once.
    private static async Task ValidateAgainstDatabaseAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;

        if (!long.TryParse(principal?.FindFirstValue(JwtClaims.Subject), out var userId))
        {
            context.Fail("The token has no valid subject.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<BankDbContext>();

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.Role, u.MustChangePassword })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || !user.IsActive || user.Role.ToString() != principal!.FindFirstValue(JwtClaims.Role))
        {
            context.Fail("The account is inactive or its role has changed.");
            return;
        }

        if (user.MustChangePassword)
            context.HttpContext.Items[PasswordChangeRequiredMiddleware.ItemKey] = true;
    }
}
