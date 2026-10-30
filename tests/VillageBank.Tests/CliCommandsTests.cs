using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VillageBank.Api.Cli;
using VillageBank.Api.Data;
using VillageBank.Api.Demo;
using VillageBank.Api.Domain;

namespace VillageBank.Tests;

[Collection("postgres")]
public class CliCommandsTests
{
    private const string DemoPassword = "Demo-Password-1";

    private readonly PostgresFixture _fixture;

    public CliCommandsTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // The other tests build their schema from the model. This one applies the real migrations
    // to an empty database, which is what a deploy does.
    [Fact]
    public async Task Migrate_builds_the_whole_schema_from_the_migrations()
    {
        var cs = await _fixture.CreateEmptyDatabaseAsync();

        var exitCode = await CliCommands.RunAsync(BuildProvider(cs), CliCommands.Migrate);

        Assert.Equal(0, exitCode);

        await using var db = _fixture.CreateDbContext(cs);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(7, await db.Accounts.CountAsync());
    }

    [Fact]
    public async Task Migrate_can_be_run_again_safely()
    {
        var cs = await _fixture.CreateEmptyDatabaseAsync();
        var provider = BuildProvider(cs);

        Assert.Equal(0, await CliCommands.RunAsync(provider, CliCommands.Migrate));
        Assert.Equal(0, await CliCommands.RunAsync(provider, CliCommands.Migrate));
    }

    [Fact]
    public async Task Seed_demo_fills_a_migrated_database()
    {
        var cs = await _fixture.CreateEmptyDatabaseAsync();
        var provider = BuildProvider(cs, DemoPassword);

        await CliCommands.RunAsync(provider, CliCommands.Migrate);
        var exitCode = await CliCommands.RunAsync(provider, CliCommands.SeedDemo);

        Assert.Equal(0, exitCode);

        await using var db = _fixture.CreateDbContext(cs);
        Assert.Equal(30, await db.Members.CountAsync());
        Assert.Equal(12, await db.Loans.CountAsync());
    }

    [Fact]
    public async Task Seed_demo_without_a_password_fails_and_writes_nothing()
    {
        var cs = await _fixture.CreateEmptyDatabaseAsync();
        var provider = BuildProvider(cs);

        await CliCommands.RunAsync(provider, CliCommands.Migrate);
        var exitCode = await CliCommands.RunAsync(provider, CliCommands.SeedDemo);

        Assert.Equal(1, exitCode);

        await using var db = _fixture.CreateDbContext(cs);
        Assert.Equal(0, await db.Members.CountAsync());
    }

    [Fact]
    public async Task An_unknown_command_is_rejected()
    {
        var cs = await _fixture.CreateEmptyDatabaseAsync();

        Assert.Equal(2, await CliCommands.RunAsync(BuildProvider(cs), "drop-everything"));
    }

    [Theory]
    [InlineData("migrate", true)]
    [InlineData("seed-demo", true)]
    [InlineData("--urls", false)]
    [InlineData("run", false)]
    public void Only_the_supported_commands_are_recognised(string argument, bool expected)
    {
        Assert.Equal(expected, CliCommands.IsCommand(argument));
    }

    private static ServiceProvider BuildProvider(string connectionString, string? demoPassword = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Seed:Demo:Password"] = demoPassword })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<BankDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddTransient<DemoDataSeeder>();

        return services.BuildServiceProvider();
    }
}
