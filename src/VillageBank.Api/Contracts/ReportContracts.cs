namespace VillageBank.Api.Contracts;

public record TrialBalanceLine(string Code, string Name, string Type, decimal Debit, decimal Credit);

public record TrialBalanceReport(
    DateOnly AsOf,
    IReadOnlyList<TrialBalanceLine> Lines,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsBalanced);

public record BalanceSheetLine(string Code, string Name, decimal Amount);

public record BalanceSheetReport(
    DateOnly AsOf,
    IReadOnlyList<BalanceSheetLine> Assets,
    IReadOnlyList<BalanceSheetLine> Liabilities,
    IReadOnlyList<BalanceSheetLine> Equity,
    decimal EarningsToDate,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    bool IsBalanced);

public record IncomeStatementLine(string Code, string Name, decimal Amount);

public record IncomeStatementReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<IncomeStatementLine> Income,
    IReadOnlyList<IncomeStatementLine> Expenses,
    decimal TotalIncome,
    decimal TotalExpenses,
    decimal NetProfit);

public record CashBookLine(
    long EntryId,
    DateTime CreatedAt,
    string Description,
    string? CreatedBy,
    decimal Receipt,
    decimal Payment,
    decimal Balance);

public record CashBookReport(
    DateOnly Date,
    decimal OpeningBalance,
    IReadOnlyList<CashBookLine> Lines,
    decimal TotalReceipts,
    decimal TotalPayments,
    decimal ClosingBalance);

public record AgingBucket(string Label, int Loans, decimal PrincipalOutstanding, decimal OverdueAmount);

public record OverdueLoan(
    long LoanId,
    long MemberId,
    string MemberName,
    decimal PrincipalOutstanding,
    decimal OverdueAmount,
    int DaysPastDue);

public record PortfolioAtRisk(string Name, int MoreThanDaysPastDue, decimal PrincipalAtRisk, decimal Percent);

public record LoanAgingReport(
    DateOnly AsOf,
    int ActiveLoans,
    decimal TotalPrincipalOutstanding,
    IReadOnlyList<AgingBucket> Buckets,
    IReadOnlyList<PortfolioAtRisk> PortfolioAtRisk,
    IReadOnlyList<OverdueLoan> OverdueLoans);

public record ReconciliationCheck(
    string Name,
    string Description,
    decimal Expected,
    decimal Actual,
    decimal Difference,
    bool IsOk);

public record ReconciliationReport(
    DateTime GeneratedAt,
    IReadOnlyList<ReconciliationCheck> Checks,
    bool AllOk);
