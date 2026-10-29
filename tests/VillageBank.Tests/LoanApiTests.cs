using System.Net;
using System.Net.Http.Json;
using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;
using static VillageBank.Tests.ApiTestSupport;

namespace VillageBank.Tests;

[Collection("postgres")]
public class LoanApiTests
{
    private readonly ApiFactory _api;

    public LoanApiTests(PostgresFixture fixture)
    {
        _api = fixture.Api;
    }

    [Fact]
    public async Task A_loan_goes_from_application_to_final_repayment_through_the_api()
    {
        var teller = await CreateActiveUserAsync(_api, UserRole.Teller);
        var manager = await CreateActiveUserAsync(_api, UserRole.Manager);
        var memberId = await CreateMemberAsync(teller.Client);

        var apply = await teller.Client.PostAsJsonAsync(
            "/api/loans", new ApplyLoanRequest(memberId, 300_000m, 2m, 10, "rice seed"), Json);
        Assert.Equal(HttpStatusCode.Created, apply.StatusCode);
        var loan = (await apply.Content.ReadFromJsonAsync<LoanDetails>(Json))!;
        Assert.Equal(LoanStatus.Pending, loan.Status);

        var approve = await manager.Client.PostAsync($"/api/loans/{loan.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        var disburse = await teller.Client.PostAsync($"/api/loans/{loan.Id}/disburse", null);
        Assert.Equal(HttpStatusCode.OK, disburse.StatusCode);
        var active = (await disburse.Content.ReadFromJsonAsync<LoanDetails>(Json))!;
        Assert.Equal(LoanStatus.Active, active.Status);
        Assert.Equal(10, active.Installments.Count);
        Assert.Equal(360_000m, active.Outstanding);

        var repay = await teller.Client.PostAsJsonAsync(
            $"/api/loans/{loan.Id}/repayments", new RepayLoanRequest(360_000m, "paid off"), Json);
        Assert.Equal(HttpStatusCode.Created, repay.StatusCode);
        var result = (await repay.Content.ReadFromJsonAsync<RepaymentResult>(Json))!;
        Assert.Equal(LoanStatus.Closed, result.LoanStatus);
        Assert.Equal(0m, result.Outstanding);
    }

    [Fact]
    public async Task Tellers_cannot_approve_loans()
    {
        var teller = await CreateActiveUserAsync(_api, UserRole.Teller);
        var memberId = await CreateMemberAsync(teller.Client);
        var loan = await ApplyAsync(teller.Client, memberId);

        var response = await teller.Client.PostAsync($"/api/loans/{loan.Id}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_manager_cannot_approve_a_loan_they_applied_for()
    {
        var manager = await CreateActiveUserAsync(_api, UserRole.Manager);
        var memberId = await CreateMemberAsync(manager.Client);
        var loan = await ApplyAsync(manager.Client, memberId);

        var response = await manager.Client.PostAsync($"/api/loans/{loan.Id}/approve", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Auditors_can_preview_a_schedule_but_not_apply_for_a_loan()
    {
        var auditor = await CreateActiveUserAsync(_api, UserRole.Auditor);

        var preview = await auditor.Client.PostAsJsonAsync(
            "/api/loans/preview", new LoanPreviewRequest(300_000m, 2m, 10), Json);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = (await preview.Content.ReadFromJsonAsync<LoanPreview>(Json))!;
        Assert.Equal(10, body.Installments.Count);
        Assert.Equal(360_000m, body.TotalRepayable);

        var apply = await auditor.Client.PostAsJsonAsync(
            "/api/loans", new ApplyLoanRequest(1, 300_000m, 2m, 10, null), Json);
        Assert.Equal(HttpStatusCode.Forbidden, apply.StatusCode);
    }

    private static async Task<long> CreateMemberAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/members", new CreateMemberRequest($"Loan Member {Guid.NewGuid():N}", null, null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<MemberDetails>(Json))!.Id;
    }

    private static async Task<LoanDetails> ApplyAsync(HttpClient client, long memberId)
    {
        var response = await client.PostAsJsonAsync(
            "/api/loans", new ApplyLoanRequest(memberId, 300_000m, 2m, 10, null), Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<LoanDetails>(Json))!;
    }
}
