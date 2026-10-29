using System.Net;
using System.Net.Http.Json;
using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;
using static VillageBank.Tests.ApiTestSupport;

namespace VillageBank.Tests;

[Collection("postgres")]
public class ReportApiTests
{
    private readonly ApiFactory _api;

    public ReportApiTests(PostgresFixture fixture)
    {
        _api = fixture.Api;
    }

    [Fact]
    public async Task Reports_need_a_token()
    {
        var response = await _api.CreateClient().GetAsync("/api/reports/trial-balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Auditor)]
    public async Task Managers_and_auditors_can_read_the_trial_balance(UserRole role)
    {
        var user = await CreateActiveUserAsync(_api, role);

        var response = await user.Client.GetAsync("/api/reports/trial-balance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = (await response.Content.ReadFromJsonAsync<TrialBalanceReport>(Json))!;
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public async Task Tellers_can_open_the_cash_book_but_not_the_other_reports()
    {
        var teller = await CreateActiveUserAsync(_api, UserRole.Teller);

        Assert.Equal(HttpStatusCode.OK, (await teller.Client.GetAsync("/api/reports/cash-book")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teller.Client.GetAsync("/api/reports/trial-balance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teller.Client.GetAsync("/api/reports/loan-aging")).StatusCode);
    }

    [Fact]
    public async Task The_super_admin_cannot_read_financial_reports()
    {
        var admin = await AdminClientAsync(_api);

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/reports/trial-balance")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/reports/cash-book")).StatusCode);
    }

    [Theory]
    [InlineData("/api/reports/balance-sheet")]
    [InlineData("/api/reports/income-statement")]
    [InlineData("/api/reports/loan-aging")]
    [InlineData("/api/reports/reconciliation")]
    public async Task Every_report_is_available_to_a_manager(string url)
    {
        var manager = await CreateActiveUserAsync(_api, UserRole.Manager);

        var response = await manager.Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_report_can_be_downloaded_as_csv()
    {
        var manager = await CreateActiveUserAsync(_api, UserRole.Manager);

        var response = await manager.Client.GetAsync("/api/reports/trial-balance?format=csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("trial-balance-", response.Content.Headers.ContentDisposition!.FileName);

        var body = (await response.Content.ReadAsStringAsync()).TrimStart('﻿');
        Assert.StartsWith("Code,Name,Type,Debit,Credit", body);
    }

    [Fact]
    public async Task An_unknown_format_is_rejected()
    {
        var manager = await CreateActiveUserAsync(_api, UserRole.Manager);

        var response = await manager.Client.GetAsync("/api/reports/trial-balance?format=pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_income_statement_period_cannot_end_before_it_starts()
    {
        var manager = await CreateActiveUserAsync(_api, UserRole.Manager);

        var response = await manager.Client.GetAsync("/api/reports/income-statement?from=2026-10-30&to=2026-10-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
