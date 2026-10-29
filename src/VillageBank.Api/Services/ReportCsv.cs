using VillageBank.Api.Contracts;

namespace VillageBank.Api.Services;

public static class ReportCsv
{
    public static string TrialBalance(TrialBalanceReport report) => CsvWriter.Build(
        new[] { "Code", "Name", "Type", "Debit", "Credit" },
        report.Lines
            .Select(l => Row(l.Code, l.Name, l.Type, l.Debit, l.Credit))
            .Append(Row("", "Total", "", report.TotalDebit, report.TotalCredit)));

    public static string BalanceSheet(BalanceSheetReport report)
    {
        var rows = new List<IReadOnlyList<object?>>();

        rows.AddRange(report.Assets.Select(l => Row("Assets", l.Code, l.Name, l.Amount)));
        rows.Add(Row("Assets", "", "Total assets", report.TotalAssets));

        rows.AddRange(report.Liabilities.Select(l => Row("Liabilities", l.Code, l.Name, l.Amount)));
        rows.Add(Row("Liabilities", "", "Total liabilities", report.TotalLiabilities));

        rows.AddRange(report.Equity.Select(l => Row("Equity", l.Code, l.Name, l.Amount)));
        rows.Add(Row("Equity", "", "Earnings to date", report.EarningsToDate));
        rows.Add(Row("Equity", "", "Total equity", report.TotalEquity));

        return CsvWriter.Build(new[] { "Section", "Code", "Name", "Amount" }, rows);
    }

    public static string IncomeStatement(IncomeStatementReport report)
    {
        var rows = new List<IReadOnlyList<object?>>();

        rows.AddRange(report.Income.Select(l => Row("Income", l.Code, l.Name, l.Amount)));
        rows.Add(Row("Income", "", "Total income", report.TotalIncome));

        rows.AddRange(report.Expenses.Select(l => Row("Expenses", l.Code, l.Name, l.Amount)));
        rows.Add(Row("Expenses", "", "Total expenses", report.TotalExpenses));

        rows.Add(Row("Result", "", "Net profit", report.NetProfit));

        return CsvWriter.Build(new[] { "Section", "Code", "Name", "Amount" }, rows);
    }

    public static string CashBook(CashBookReport report)
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            Row(report.Date, "", "Opening balance", "", "", "", report.OpeningBalance)
        };

        rows.AddRange(report.Lines.Select(l =>
            Row(l.CreatedAt, l.EntryId, l.Description, l.CreatedBy, l.Receipt, l.Payment, l.Balance)));

        rows.Add(Row(report.Date, "", "Closing balance", "", report.TotalReceipts, report.TotalPayments, report.ClosingBalance));

        return CsvWriter.Build(
            new[] { "Time", "Entry", "Description", "By", "Receipt", "Payment", "Balance" }, rows);
    }

    public static string LoanAging(LoanAgingReport report) => CsvWriter.Build(
        new[] { "Loan", "Member id", "Member", "Principal outstanding", "Overdue amount", "Days past due", "Bucket" },
        report.OverdueLoans.Select(l => Row(
            l.LoanId, l.MemberId, l.MemberName, l.PrincipalOutstanding, l.OverdueAmount, l.DaysPastDue,
            ReportService.BucketLabel(l.DaysPastDue))));

    public static string Reconciliation(ReconciliationReport report) => CsvWriter.Build(
        new[] { "Check", "Expected", "Actual", "Difference", "OK" },
        report.Checks.Select(c => Row(c.Name, c.Expected, c.Actual, c.Difference, c.IsOk)));

    private static IReadOnlyList<object?> Row(params object?[] cells) => cells;
}
