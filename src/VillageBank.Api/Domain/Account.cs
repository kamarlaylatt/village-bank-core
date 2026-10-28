namespace VillageBank.Api.Domain;

public class Account
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public bool IsActive { get; set; } = true;
}
