namespace VillageBank.Api.Domain;

public static class JournalEntryValidator
{
    public static void Validate(string description, IReadOnlyList<LineInput> lines)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new LedgerException("Description is required.");

        if (lines.Count < 2)
            throw new LedgerException("A journal entry needs at least 2 lines.");

        foreach (var line in lines)
        {
            if (line.Debit < 0 || line.Credit < 0)
                throw new LedgerException("Amounts cannot be negative.");

            if ((line.Debit > 0) == (line.Credit > 0))
                throw new LedgerException("Each line must have either a debit or a credit, not both and not neither.");

            if (decimal.Round(line.Debit, 4) != line.Debit || decimal.Round(line.Credit, 4) != line.Credit)
                throw new LedgerException("Amounts can have at most 4 decimal places.");
        }

        var totalDebit = lines.Sum(l => l.Debit);
        var totalCredit = lines.Sum(l => l.Credit);

        if (totalDebit != totalCredit)
            throw new LedgerException($"Entry is not balanced: debit {totalDebit} != credit {totalCredit}.");
    }
}
