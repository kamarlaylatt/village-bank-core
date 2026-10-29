using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Auth;

public class SuperAdminSeeder : IHostedService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly TimeProvider _clock;
    private readonly ILogger<SuperAdminSeeder> _logger;

    public SuperAdminSeeder(
        IServiceScopeFactory scopes,
        IConfiguration config,
        TimeProvider clock,
        ILogger<SuperAdminSeeder> logger)
    {
        _scopes = scopes;
        _config = config;
        _clock = clock;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        if (await db.Users.AnyAsync(u => u.Role == UserRole.SuperAdmin, cancellationToken))
            return;

        var configuredUsername = _config["Seed:SuperAdmin:Username"];
        var password = _config["Seed:SuperAdmin:Password"];

        if (string.IsNullOrWhiteSpace(configuredUsername) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning(
                "No SuperAdmin exists and Seed:SuperAdmin:Username / Seed:SuperAdmin:Password are not set. Nobody will be able to log in.");
            return;
        }

        var username = UsernamePolicy.NormalizeAndValidate(configuredUsername);
        PasswordPolicy.Validate(password, username);

        var user = new User
        {
            Username = username,
            Role = UserRole.SuperAdmin,
            IsActive = true,
            MustChangePassword = false,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = hasher.HashPassword(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded SuperAdmin account {Username}.", username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
