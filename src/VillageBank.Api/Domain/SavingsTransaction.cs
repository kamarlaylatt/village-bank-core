namespace VillageBank.Api.Domain;

public class SavingsTransaction
{
    public long Id { get; set; }

    public long SavingsAccountId { get; set; }
    public SavingsAccount SavingsAccount { get; set; } = null!;

    public SavingsTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? Note { get; set; }

    public long JournalEntryId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;

    public DateTime CreatedAt { get; set; }
}
