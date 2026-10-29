using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Auth;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

// Roles are set per action: managers and auditors read every report, tellers only the cash book.
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IReportService _reports;
    private readonly TimeProvider _clock;

    public ReportsController(IReportService reports, TimeProvider clock)
    {
        _reports = reports;
        _clock = clock;
    }

    private DateOnly Today => BusinessTime.Today(_clock);

    [HttpGet("trial-balance")]
    [Authorize(Roles = Roles.Reporters)]
    public async Task<IActionResult> TrialBalance(
        [FromQuery] DateOnly? asOf, [FromQuery] string? format, CancellationToken ct)
    {
        var csv = IsCsv(format);
        var date = asOf ?? Today;

        var report = await _reports.TrialBalanceAsync(date, ct);

        return Respond(csv, report, ReportCsv.TrialBalance, $"trial-balance-{date:yyyy-MM-dd}");
    }

    [HttpGet("balance-sheet")]
    [Authorize(Roles = Roles.Reporters)]
    public async Task<IActionResult> BalanceSheet(
        [FromQuery] DateOnly? asOf, [FromQuery] string? format, CancellationToken ct)
    {
        var csv = IsCsv(format);
        var date = asOf ?? Today;

        var report = await _reports.BalanceSheetAsync(date, ct);

        return Respond(csv, report, ReportCsv.BalanceSheet, $"balance-sheet-{date:yyyy-MM-dd}");
    }

    [HttpGet("income-statement")]
    [Authorize(Roles = Roles.Reporters)]
    public async Task<IActionResult> IncomeStatement(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? format, CancellationToken ct)
    {
        var csv = IsCsv(format);

        var end = to ?? Today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);

        if (start > end)
            throw new BusinessRuleException("'from' must not be after 'to'.");

        var report = await _reports.IncomeStatementAsync(start, end, ct);

        return Respond(csv, report, ReportCsv.IncomeStatement, $"income-statement-{start:yyyy-MM-dd}-to-{end:yyyy-MM-dd}");
    }

    [HttpGet("cash-book")]
    [Authorize(Roles = Roles.Staff)]
    public async Task<IActionResult> CashBook(
        [FromQuery] DateOnly? date, [FromQuery] string? format, CancellationToken ct)
    {
        var csv = IsCsv(format);
        var day = date ?? Today;

        var report = await _reports.CashBookAsync(day, ct);

        return Respond(csv, report, ReportCsv.CashBook, $"cash-book-{day:yyyy-MM-dd}");
    }

    [HttpGet("loan-aging")]
    [Authorize(Roles = Roles.Reporters)]
    public async Task<IActionResult> LoanAging([FromQuery] string? format, CancellationToken ct)
    {
        var csv = IsCsv(format);

        var report = await _reports.LoanAgingAsync(ct);

        return Respond(csv, report, ReportCsv.LoanAging, $"loan-aging-{report.AsOf:yyyy-MM-dd}");
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = Roles.Reporters)]
    public async Task<IActionResult> Reconciliation([FromQuery] string? format, CancellationToken ct)
    {
        var csv = IsCsv(format);

        var report = await _reports.ReconciliationAsync(ct);

        return Respond(csv, report, ReportCsv.Reconciliation, $"reconciliation-{report.GeneratedAt:yyyy-MM-dd}");
    }

    // Checked before the report runs, so a mistyped format fails fast instead of after a slow query.
    private static bool IsCsv(string? format) => format?.ToLowerInvariant() switch
    {
        null or "" or "json" => false,
        "csv" => true,
        _ => throw new BusinessRuleException("Format must be 'json' or 'csv'.")
    };

    private IActionResult Respond<T>(bool csv, T report, Func<T, string> toCsv, string fileName) =>
        csv
            ? File(CsvWriter.ToUtf8WithBom(toCsv(report)), "text/csv; charset=utf-8", fileName + ".csv")
            : Ok(report);
}
