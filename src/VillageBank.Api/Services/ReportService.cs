using Dapper;
using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class ReportService : IReportService
{
    // Total debits and credits of every account from the journal, up to and including a date.
    // The date filter sits inside the sub-select: putting it in a LEFT JOIN condition would
    // still count lines whose entry is too new.
    private const string AccountBalancesSql = """
        SELECT a.code AS "Code", a.name AS "Name", a.type AS "Type",
               COALESCE(t.total_debit, 0)  AS "TotalDebit",
               COALESCE(t.total_credit, 0) AS "TotalCredit"
        FROM accounts a
        LEFT JOIN (
            SELECT l.account_id, SUM(l.debit) AS total_debit, SUM(l.credit) AS total_credit
            FROM journal_lines l
            JOIN journal_entries e ON e.id = l.journal_entry_id
            WHERE e.entry_date <= @asOf::date
            GROUP BY l.account_id
        ) t ON t.account_id = a.id
        ORDER BY a.code
        """;

    private const string IncomeStatementSql = """
        SELECT a.code AS "Code", a.name AS "Name", a.type AS "Type",
               SUM(l.credit - l.debit) AS "NetCredit"
        FROM journal_lines l
        JOIN journal_entries e ON e.id = l.journal_entry_id
        JOIN accounts a ON a.id = l.account_id
        WHERE a.type IN ('Income', 'Expense')
          AND e.entry_date BETWEEN @from::date AND @to::date
        GROUP BY a.id, a.code, a.name, a.type
        ORDER BY a.code
        """;

    private const string CashOpeningSql = """
        SELECT COALESCE(SUM(l.debit - l.credit), 0)
        FROM journal_lines l
        JOIN journal_entries e ON e.id = l.journal_entry_id
        JOIN accounts a ON a.id = l.account_id
        WHERE a.code = @cash AND e.entry_date < @date::date
        """;

    private const string CashLinesSql = """
        SELECT e.id AS "EntryId", e.created_at AS "CreatedAt", e.description AS "Description",
               u.username AS "CreatedBy", l.debit AS "Receipt", l.credit AS "Payment"
        FROM journal_lines l
        JOIN journal_entries e ON e.id = l.journal_entry_id
        JOIN accounts a ON a.id = l.account_id
        LEFT JOIN users u ON u.id = e.created_by_user_id
        WHERE a.code = @cash AND e.entry_date = @date::date
        ORDER BY e.created_at, e.id, l.id
        """;

    // One row per active loan. "Days past due" is the age of the oldest installment that is
    // overdue and not fully paid, which is how portfolio-at-risk is normally measured.
    private const string LoanAgingSql = """
        SELECT l.id AS "LoanId", l.member_id AS "MemberId", m.full_name AS "MemberName",
               SUM(i.principal_due - i.principal_paid) AS "PrincipalOutstanding",
               COALESCE(SUM(CASE WHEN i.due_date < @today::date
                                 THEN (i.principal_due + i.interest_due) - (i.principal_paid + i.interest_paid)
                            END), 0) AS "OverdueAmount",
               COALESCE(MAX(CASE WHEN i.due_date < @today::date
                                  AND (i.principal_due + i.interest_due) > (i.principal_paid + i.interest_paid)
                                 THEN @today::date - i.due_date
                            END), 0) AS "DaysPastDue"
        FROM loans l
        JOIN members m ON m.id = l.member_id
        JOIN loan_installments i ON i.loan_id = l.id
        WHERE l.status = 'Active'
        GROUP BY l.id, l.member_id, m.full_name
        ORDER BY l.id
        """;

    private const string LedgerBalanceSql = """
        SELECT COALESCE(SUM(l.debit - l.credit), 0)
        FROM journal_lines l
        JOIN accounts a ON a.id = l.account_id
        WHERE a.code = @code
        """;

    private static readonly (string Label, int From, int To)[] BucketDefinitions =
    {
        ("Current", 0, 0),
        ("1-30", 1, 30),
        ("31-60", 31, 60),
        ("61-90", 61, 90),
        ("90+", 91, int.MaxValue)
    };

    private readonly BankDbContext _db;
    private readonly TimeProvider _clock;

    public ReportService(BankDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public static string BucketLabel(int daysPastDue) =>
        BucketDefinitions.First(b => daysPastDue >= b.From && daysPastDue <= b.To).Label;

    public async Task<TrialBalanceReport> TrialBalanceAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var rows = await QueryAsync<AccountBalanceRow>(AccountBalancesSql, new { asOf = ToDateTime(asOf) }, ct);

        var lines = rows
            .Select(r =>
            {
                var net = r.TotalDebit - r.TotalCredit;
                return new TrialBalanceLine(r.Code, r.Name, r.Type, net > 0 ? net : 0m, net < 0 ? -net : 0m);
            })
            .ToList();

        var totalDebit = lines.Sum(l => l.Debit);
        var totalCredit = lines.Sum(l => l.Credit);

        return new TrialBalanceReport(asOf, lines, totalDebit, totalCredit, totalDebit == totalCredit);
    }

    public async Task<BalanceSheetReport> BalanceSheetAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var rows = await QueryAsync<AccountBalanceRow>(AccountBalancesSql, new { asOf = ToDateTime(asOf) }, ct);

        // Assets and expenses grow with debits; liabilities, equity and income grow with credits.
        List<BalanceSheetLine> Section(string type, bool debitNormal) => rows
            .Where(r => r.Type == type)
            .Select(r => new BalanceSheetLine(
                r.Code, r.Name, debitNormal ? r.TotalDebit - r.TotalCredit : r.TotalCredit - r.TotalDebit))
            .Where(l => l.Amount != 0)
            .ToList();

        var assets = Section("Asset", debitNormal: true);
        var liabilities = Section("Liability", debitNormal: false);
        var equity = Section("Equity", debitNormal: false);

        // The books are not closed at year end, so income less expenses to date is shown as part of equity.
        var earnings = Section("Income", debitNormal: false).Sum(l => l.Amount)
                       - Section("Expense", debitNormal: true).Sum(l => l.Amount);

        var totalAssets = assets.Sum(l => l.Amount);
        var totalLiabilities = liabilities.Sum(l => l.Amount);
        var totalEquity = equity.Sum(l => l.Amount) + earnings;

        return new BalanceSheetReport(
            asOf, assets, liabilities, equity, earnings,
            totalAssets, totalLiabilities, totalEquity,
            totalAssets == totalLiabilities + totalEquity);
    }

    public async Task<IncomeStatementReport> IncomeStatementAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await QueryAsync<IncomeRow>(
            IncomeStatementSql, new { from = ToDateTime(from), to = ToDateTime(to) }, ct);

        var income = rows
            .Where(r => r.Type == "Income")
            .Select(r => new IncomeStatementLine(r.Code, r.Name, r.NetCredit))
            .ToList();

        var expenses = rows
            .Where(r => r.Type == "Expense")
            .Select(r => new IncomeStatementLine(r.Code, r.Name, -r.NetCredit))
            .ToList();

        var totalIncome = income.Sum(l => l.Amount);
        var totalExpenses = expenses.Sum(l => l.Amount);

        return new IncomeStatementReport(from, to, income, expenses, totalIncome, totalExpenses, totalIncome - totalExpenses);
    }

    public async Task<CashBookReport> CashBookAsync(DateOnly date, CancellationToken ct = default)
    {
        var parameters = new { cash = AccountCodes.Cash, date = ToDateTime(date) };

        var opening = await ScalarAsync<decimal>(CashOpeningSql, parameters, ct);
        var rows = await QueryAsync<CashRow>(CashLinesSql, parameters, ct);

        var lines = new List<CashBookLine>(rows.Count);
        var balance = opening;

        foreach (var row in rows)
        {
            balance += row.Receipt - row.Payment;
            lines.Add(new CashBookLine(
                row.EntryId, row.CreatedAt, row.Description, row.CreatedBy, row.Receipt, row.Payment, balance));
        }

        return new CashBookReport(
            date, opening, lines, lines.Sum(l => l.Receipt), lines.Sum(l => l.Payment), balance);
    }

    public async Task<LoanAgingReport> LoanAgingAsync(CancellationToken ct = default)
    {
        var today = BusinessTime.Today(_clock);
        var rows = await QueryAsync<LoanAgingRow>(LoanAgingSql, new { today = ToDateTime(today) }, ct);

        var totalPrincipal = rows.Sum(r => r.PrincipalOutstanding);

        var buckets = BucketDefinitions
            .Select(b =>
            {
                var inBucket = rows.Where(r => r.DaysPastDue >= b.From && r.DaysPastDue <= b.To).ToList();
                return new AgingBucket(
                    b.Label,
                    inBucket.Count,
                    inBucket.Sum(r => r.PrincipalOutstanding),
                    inBucket.Sum(r => r.OverdueAmount));
            })
            .ToList();

        // PAR(n): outstanding principal of loans more than n days late, as a share of the whole portfolio.
        var par = new[] { 1, 30, 60, 90 }
            .Select(days =>
            {
                var atRisk = rows.Where(r => r.DaysPastDue > days).Sum(r => r.PrincipalOutstanding);
                var percent = totalPrincipal == 0 ? 0m : Math.Round(atRisk * 100m / totalPrincipal, 2);
                return new PortfolioAtRisk($"PAR{days}", days, atRisk, percent);
            })
            .ToList();

        var overdue = rows
            .Where(r => r.DaysPastDue > 0)
            .OrderByDescending(r => r.DaysPastDue)
            .Select(r => new OverdueLoan(
                r.LoanId, r.MemberId, r.MemberName, r.PrincipalOutstanding, r.OverdueAmount, r.DaysPastDue))
            .ToList();

        return new LoanAgingReport(today, rows.Count, totalPrincipal, buckets, par, overdue);
    }

    public async Task<ReconciliationReport> ReconciliationAsync(CancellationToken ct = default)
    {
        var savingsAccounts = await ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(balance), 0) FROM savings_accounts", null, ct);
        var savingsLedger = -await ScalarAsync<decimal>(
            LedgerBalanceSql, new { code = AccountCodes.MemberSavings }, ct);

        var loanBalances = await ScalarAsync<decimal>(
            "SELECT COALESCE(SUM(principal_due - principal_paid), 0) FROM loan_installments", null, ct);
        var loansLedger = await ScalarAsync<decimal>(
            LedgerBalanceSql, new { code = AccountCodes.LoansReceivable }, ct);

        var totalDebit = await ScalarAsync<decimal>("SELECT COALESCE(SUM(debit), 0) FROM journal_lines", null, ct);
        var totalCredit = await ScalarAsync<decimal>("SELECT COALESCE(SUM(credit), 0) FROM journal_lines", null, ct);

        var unbalanced = await ScalarAsync<long>(
            """
            SELECT COUNT(*) FROM (
                SELECT 1 FROM journal_lines GROUP BY journal_entry_id HAVING SUM(debit) <> SUM(credit)
            ) x
            """,
            null, ct);

        var checks = new List<ReconciliationCheck>
        {
            Check("Member savings",
                "Sum of member savings balances against the Member Savings ledger account (2000).",
                savingsAccounts, savingsLedger),
            Check("Loans receivable",
                "Principal still owed on loans against the Loans Receivable ledger account (1200).",
                loanBalances, loansLedger),
            Check("Ledger totals",
                "Total debits against total credits across the whole journal.",
                totalDebit, totalCredit),
            Check("Unbalanced journal entries",
                "Journal entries whose debits and credits differ. Should be none.",
                0m, unbalanced)
        };

        return new ReconciliationReport(_clock.GetUtcNow().UtcDateTime, checks, checks.All(c => c.IsOk));
    }

    private static ReconciliationCheck Check(string name, string description, decimal expected, decimal actual) =>
        new(name, description, expected, actual, actual - expected, actual == expected);

    // Dapper runs the SQL on the connection the DbContext already owns and opens it when needed.
    private async Task<List<T>> QueryAsync<T>(string sql, object? parameters, CancellationToken ct)
    {
        var rows = await _db.Database.GetDbConnection()
            .QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: ct));

        return rows.ToList();
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters, CancellationToken ct) =>
        (await _db.Database.GetDbConnection()
            .ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: ct)))!;

    private static DateTime ToDateTime(DateOnly date) => date.ToDateTime(TimeOnly.MinValue);

    internal sealed class AccountBalanceRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
    }

    internal sealed class IncomeRow
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public decimal NetCredit { get; set; }
    }

    internal sealed class CashRow
    {
        public long EntryId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Description { get; set; } = "";
        public string? CreatedBy { get; set; }
        public decimal Receipt { get; set; }
        public decimal Payment { get; set; }
    }

    internal sealed class LoanAgingRow
    {
        public long LoanId { get; set; }
        public long MemberId { get; set; }
        public string MemberName { get; set; } = "";
        public decimal PrincipalOutstanding { get; set; }
        public decimal OverdueAmount { get; set; }
        public int DaysPastDue { get; set; }
    }
}
