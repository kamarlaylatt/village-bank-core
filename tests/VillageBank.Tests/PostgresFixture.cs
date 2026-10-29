using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using VillageBank.Api.Data;

namespace VillageBank.Tests;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    private ApiFactory? _api;

    public string ConnectionString => _container.GetConnectionString();

    // Created lazily so tests that only need the database do not start the web host.
    public ApiFactory Api => _api ??= new ApiFactory(ConnectionString);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        _api?.Dispose();
        await _container.DisposeAsync();
    }

    public BankDbContext CreateDbContext(string? connectionString = null)
    {
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(connectionString ?? ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new BankDbContext(options);
    }

    // A separate, empty database for tests whose assertions depend on everything in the ledger,
    // such as report totals, which would otherwise include data written by other tests.
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var name = $"isolated_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var isolated = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = name }.ConnectionString;

        await using var db = CreateDbContext(isolated);
        await db.Database.EnsureCreatedAsync();

        return isolated;
    }
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
