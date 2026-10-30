using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Data;
using VillageBank.Api.Demo;

namespace VillageBank.Api.Cli;

// One-off administrative commands: `dotnet VillageBank.Api.dll migrate` and `... seed-demo`.
// They read the same configuration as the API but never start the web server, so each one can be
// its own deploy step, for example `docker compose run --rm api migrate`. The container has no
// `dotnet ef` tool, which is why migrating is built into the app.
public static class CliCommands
{
    public const string Migrate = "migrate";
    public const string SeedDemo = "seed-demo";

    public static bool IsCommand(string argument) => argument is Migrate or SeedDemo;

    // Returns the process exit code: 0 done, 1 failed, 2 unknown command.
    public static async Task<int> RunAsync(IServiceProvider services, string command, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("VillageBank.Cli");

        try
        {
            switch (command)
            {
                case Migrate:
                {
                    var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
                    var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();

                    await db.Database.MigrateAsync(ct);

                    logger.LogInformation("Database is up to date ({Count} migration(s) applied).", pending);
                    return 0;
                }

                case SeedDemo:
                {
                    var result = await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(ct);

                    if (!result.Seeded)
                        logger.LogWarning("Nothing was created: the database already has members.");

                    return 0;
                }

                default:
                    logger.LogError("Unknown command '{Command}'. Use '{Migrate}' or '{SeedDemo}'.", command, Migrate, SeedDemo);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command '{Command}' failed: {Message}", command, ex.Message);
            return 1;
        }
    }
}
