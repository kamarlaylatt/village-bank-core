using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Controllers;
using VillageBank.Api.Domain;

namespace VillageBank.Tests;

[Collection("postgres")]
public class AuthIntegrationTests
{
    private const string TemporaryPassword = "Temp-Password-1";
    private const string FinalPassword = "Final-Password-1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgresFixture _fixture;
    private readonly ApiFactory _api;

    public AuthIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _api = fixture.Api;
    }

    [Fact]
    public async Task Requests_without_a_token_are_unauthorized()
    {
        var response = await _api.CreateClient().GetAsync("/api/members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_a_wrong_password_is_unauthorized()
    {
        var response = await _api.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(ApiFactory.AdminUsername, "wrong-password-1"), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_an_unknown_user_is_unauthorized()
    {
        var response = await _api.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest("nobody-here", "whatever-123"), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_manages_users_but_cannot_touch_financial_data()
    {
        var admin = await AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/members")).StatusCode);
    }

    [Fact]
    public async Task SuperAdmin_accounts_cannot_be_created_through_the_api()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(NewUsername("root"), UserRole.SuperAdmin, TemporaryPassword), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Weak_temporary_passwords_are_rejected()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(NewUsername("teller"), UserRole.Teller, "short"), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_usernames_are_rejected()
    {
        var admin = await AdminClientAsync();
        var username = NewUsername("teller");

        var first = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(username, UserRole.Teller, TemporaryPassword), Json);
        var second = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(username, UserRole.Teller, TemporaryPassword), Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Tellers_cannot_manage_users()
    {
        var teller = await CreateActiveUserAsync(UserRole.Teller);

        var response = await teller.Client.GetAsync("/api/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task New_users_must_change_their_temporary_password_first()
    {
        var admin = await AdminClientAsync();
        var username = NewUsername("teller");

        await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(username, UserRole.Teller, TemporaryPassword), Json);

        var client = await LoginAsync(username, TemporaryPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/members")).StatusCode);

        var changed = await client.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(TemporaryPassword, FinalPassword), Json);
        changed.EnsureSuccessStatusCode();

        var login = (await changed.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        Assert.False(login.MustChangePassword);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/members")).StatusCode);
    }

    [Fact]
    public async Task Auditors_can_read_but_not_write()
    {
        var auditor = await CreateActiveUserAsync(UserRole.Auditor);

        Assert.Equal(HttpStatusCode.OK, (await auditor.Client.GetAsync("/api/members")).StatusCode);

        var create = await auditor.Client.PostAsJsonAsync(
            "/api/members", new CreateMemberRequest("Nobody", null, null), Json);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Tellers_can_open_members_and_deposit_and_the_actions_are_attributed_to_them()
    {
        var teller = await CreateActiveUserAsync(UserRole.Teller);

        var created = await teller.Client.PostAsJsonAsync(
            "/api/members", new CreateMemberRequest($"Ma Aye {Guid.NewGuid():N}", null, null), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var member = (await created.Content.ReadFromJsonAsync<MemberDetails>(Json))!;

        var deposit = await teller.Client.PostAsJsonAsync(
            $"/api/savings-accounts/{member.SavingsAccountId}/deposits", new MoneyRequest(50_000m, null), Json);
        Assert.Equal(HttpStatusCode.Created, deposit.StatusCode);
        var result = (await deposit.Content.ReadFromJsonAsync<SavingsTransactionResult>(Json))!;

        await using var db = _fixture.CreateDbContext();
        var transaction = await db.SavingsTransactions
            .AsNoTracking()
            .Include(t => t.JournalEntry)
            .SingleAsync(t => t.Id == result.TransactionId);

        Assert.Equal(teller.Id, transaction.CreatedByUserId);
        Assert.Equal(teller.Id, transaction.JournalEntry.CreatedByUserId);
    }

    [Fact]
    public async Task Only_managers_can_post_journal_entries_directly()
    {
        var teller = await CreateActiveUserAsync(UserRole.Teller);
        var manager = await CreateActiveUserAsync(UserRole.Manager);

        // An entry with no lines is invalid, so a manager reaching the service gets 400, not 403.
        var request = new PostJournalEntryRequest(new DateOnly(2026, 10, 30), "test", new List<LineInput>());

        var asTeller = await teller.Client.PostAsJsonAsync("/api/journal-entries", request, Json);
        var asManager = await manager.Client.PostAsJsonAsync("/api/journal-entries", request, Json);

        Assert.Equal(HttpStatusCode.Forbidden, asTeller.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, asManager.StatusCode);
    }

    [Fact]
    public async Task Deactivated_users_lose_access_immediately()
    {
        var teller = await CreateActiveUserAsync(UserRole.Teller);
        Assert.Equal(HttpStatusCode.OK, (await teller.Client.GetAsync("/api/members")).StatusCode);

        var admin = await AdminClientAsync();
        (await admin.PostAsync($"/api/users/{teller.Id}/deactivate", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await teller.Client.GetAsync("/api/members")).StatusCode);

        var login = await _api.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(teller.Username, FinalPassword), Json);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Changing_a_role_invalidates_existing_tokens()
    {
        var teller = await CreateActiveUserAsync(UserRole.Teller);

        var admin = await AdminClientAsync();
        var response = await admin.PutAsJsonAsync(
            $"/api/users/{teller.Id}/role", new ChangeRoleRequest(UserRole.Auditor), Json);
        response.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await teller.Client.GetAsync("/api/members")).StatusCode);
    }

    private record StaffSession(long Id, string Username, HttpClient Client);

    private static string NewUsername(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..20];

    private async Task<HttpClient> LoginAsync(string username, string password)
    {
        var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password), Json);
        response.EnsureSuccessStatusCode();

        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return client;
    }

    private Task<HttpClient> AdminClientAsync() =>
        LoginAsync(ApiFactory.AdminUsername, ApiFactory.AdminPassword);

    private async Task<StaffSession> CreateActiveUserAsync(UserRole role)
    {
        var admin = await AdminClientAsync();
        var username = NewUsername(role.ToString().ToLowerInvariant());

        var created = await admin.PostAsJsonAsync(
            "/api/users", new CreateUserRequest(username, role, TemporaryPassword), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await created.Content.ReadFromJsonAsync<UserDetails>(Json))!;

        var client = await LoginAsync(username, TemporaryPassword);

        var changed = await client.PostAsJsonAsync(
            "/api/auth/change-password", new ChangePasswordRequest(TemporaryPassword, FinalPassword), Json);
        changed.EnsureSuccessStatusCode();

        var login = (await changed.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        return new StaffSession(user.Id, username, client);
    }
}
