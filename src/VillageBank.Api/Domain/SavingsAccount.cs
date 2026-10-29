namespace VillageBank.Api.Domain;

public class SavingsAccount
{
    public long Id { get; set; }

    public long MemberId { get; set; }
    public Member Member { get; set; } = null!;

    public decimal Balance { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime OpenedAt { get; set; }
}
