using VillageBank.Api.Domain;

namespace VillageBank.Tests;

public class JournalEntryValidatorTests
{
    private static List<LineInput> Balanced() => new()
    {
        new LineInput(1, 50_000m, 0m),
        new LineInput(3, 0m, 50_000m)
    };

    [Fact]
    public void Balanced_entry_is_accepted()
    {
        var ex = Record.Exception(() => JournalEntryValidator.Validate("Deposit", Balanced()));
        Assert.Null(ex);
    }

    [Fact]
    public void Unbalanced_entry_is_rejected()
    {
        var lines = new List<LineInput> { new(1, 50_000m, 0m), new(3, 0m, 40_000m) };
        var ex = Assert.Throws<LedgerException>(() => JournalEntryValidator.Validate("Deposit", lines));
        Assert.Contains("not balanced", ex.Message);
    }

    [Fact]
    public void Single_line_is_rejected()
    {
        var lines = new List<LineInput> { new(1, 50_000m, 0m) };
        Assert.Throws<LedgerException>(() => JournalEntryValidator.Validate("Deposit", lines));
    }

    [Fact]
    public void Line_with_both_debit_and_credit_is_rejected()
    {
        var lines = new List<LineInput> { new(1, 10m, 10m), new(3, 0m, 0m) };
        Assert.Throws<LedgerException>(() => JournalEntryValidator.Validate("Bad", lines));
    }

    [Fact]
    public void Negative_amount_is_rejected()
    {
        var lines = new List<LineInput> { new(1, -50m, 0m), new(3, 0m, -50m) };
        Assert.Throws<LedgerException>(() => JournalEntryValidator.Validate("Bad", lines));
    }

    [Fact]
    public void Empty_description_is_rejected()
    {
        Assert.Throws<LedgerException>(() => JournalEntryValidator.Validate("  ", Balanced()));
    }

    [Fact]
    public void Entry_with_three_lines_is_accepted_when_balanced()
    {
        // ချေးငွေပြန်ဆပ်: Cash 106,000 = Loans 100,000 + Interest 6,000
        var lines = new List<LineInput> { new(1, 106_000m, 0m), new(2, 0m, 100_000m), new(5, 0m, 6_000m) };
        var ex = Record.Exception(() => JournalEntryValidator.Validate("Repayment", lines));
        Assert.Null(ex);
    }
}
