namespace VillageBank.Api.Domain;

public class Loan
{
    public long Id { get; set; }

    public long MemberId { get; set; }
    public Member Member { get; set; } = null!;

    public decimal Principal { get; set; }
    public decimal MonthlyInterestRatePercent { get; set; }
    public int TermMonths { get; set; }
    public string? Purpose { get; set; }

    public LoanStatus Status { get; set; }

    public DateTime AppliedAt { get; set; }
    public long? AppliedByUserId { get; set; }

    public DateTime? DecidedAt { get; set; }
    public long? DecidedByUserId { get; set; }
    public string? RejectionReason { get; set; }

    public DateOnly? DisbursedOn { get; set; }
    public long? DisbursedByUserId { get; set; }
    public long? DisbursementJournalEntryId { get; set; }
    public JournalEntry? DisbursementJournalEntry { get; set; }

    public DateTime? ClosedAt { get; set; }

    public List<LoanInstallment> Installments { get; set; } = new();
}
