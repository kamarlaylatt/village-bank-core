namespace VillageBank.Api.Domain;

public class LedgerException : Exception
{
    public LedgerException(string message) : base(message) { }
}
