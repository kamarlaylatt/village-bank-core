using System.Net;

namespace VillageBank.Tests;

[Collection("postgres")]
public class HealthApiTests
{
    private readonly ApiFactory _api;

    public HealthApiTests(PostgresFixture fixture)
    {
        _api = fixture.Api;
    }

    [Fact]
    public async Task The_health_endpoint_is_open_and_checks_the_database()
    {
        var response = await _api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
