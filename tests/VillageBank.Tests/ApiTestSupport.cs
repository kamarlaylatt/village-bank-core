using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;

namespace VillageBank.Tests;

public record StaffSession(long Id, string Username, HttpClient Client);

public static class ApiTestSupport
{
    public const string TemporaryPassword = "Temp-Password-1";
    public const string FinalPassword = "Final-Password-1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static string NewUsername(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    public static async Task<HttpClient> LoginAsync(ApiFactory api, string username, string password)
    {
        var client = api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password), Json);
        response.EnsureSuccessStatusCode();

        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return client;
    }

    public static Task<HttpClient> AdminClientAsync(ApiFactory api) =>
        LoginAsync(api, ApiFactory.AdminUsername, ApiFactory.AdminPassword);

    // Creates a user as the SuperAdmin, then signs in and changes the temporary password,
    // which is what a real new employee has to do before the system lets them in.
    public static async Task<StaffSession> CreateActiveUserAsync(ApiFactory api, UserRole role)
    {
        var admin = await AdminClientAsync(api);
        var username = NewUsername(role.ToString().ToLowerInvariant());

        var created = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(username, role, TemporaryPassword), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await created.Content.ReadFromJsonAsync<UserDetails>(Json))!;

        var client = await LoginAsync(api, username, TemporaryPassword);

        var changed = await client.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(TemporaryPassword, FinalPassword), Json);
        changed.EnsureSuccessStatusCode();

        var login = (await changed.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return new StaffSession(user.Id, username, client);
    }
}
