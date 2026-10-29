namespace VillageBank.Api.Domain;

public class JournalEntry
{
    public long Id { get; set; }
    public DateOnly EntryDate { get; set; }
    public string Description { get; set; } = "";
    public string? SourceType { get; set; }   // 'savings_transaction', 'loan_repayment' ... (နောက်မှ သုံးမယ်)
    public long? SourceId { get; set; }
    public long? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }   // UTC

    public List<JournalLine> Lines { get; set; } = new();
}
