using Microsoft.EntityFrameworkCore;
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

    public BankDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new BankDbContext(options);
    }
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
