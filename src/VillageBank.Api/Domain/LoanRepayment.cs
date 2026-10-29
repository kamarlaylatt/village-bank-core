namespace VillageBank.Api.Domain;

public class LoanRepayment
{
    public long Id { get; set; }

    public long LoanId { get; set; }
    public Loan Loan { get; set; } = null!;

    public decimal Amount { get; set; }
    public decimal PrincipalPart { get; set; }
    public decimal InterestPart { get; set; }
    public DateOnly PaidOn { get; set; }

    public string? IdempotencyKey { get; set; }
    public string? Note { get; set; }

    public long JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;

    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}
