namespace VillageBank.Api.Domain;

public class LedgerException : BusinessRuleException
{
    public LedgerException(string message) : base(message) { }
}
