namespace VillageBank.Api.Domain;

public class Member
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Village { get; set; }
    public DateOnly JoinedOn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public SavingsAccount? SavingsAccount { get; set; }
}
