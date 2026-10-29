using VillageBank.Api.Contracts;

namespace VillageBank.Api.Services;

public interface IReportService
{
    Task<TrialBalanceReport> TrialBalanceAsync(DateOnly asOf, CancellationToken ct = default);

    Task<BalanceSheetReport> BalanceSheetAsync(DateOnly asOf, CancellationToken ct = default);

    Task<IncomeStatementReport> IncomeStatementAsync(DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<CashBookReport> CashBookAsync(DateOnly date, CancellationToken ct = default);

    Task<LoanAgingReport> LoanAgingAsync(CancellationToken ct = default);

    Task<ReconciliationReport> ReconciliationAsync(CancellationToken ct = default);
}
