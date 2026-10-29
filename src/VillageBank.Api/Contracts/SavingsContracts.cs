namespace VillageBank.Api.Contracts;

public record MoneyRequest(decimal Amount, string? Note);

public record SavingsAccountSummary(long Id, long MemberId, string MemberName, decimal Balance, bool IsActive);

public record SavingsTransactionResult(
    long TransactionId,
    long SavingsAccountId,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    long JournalEntryId,
    DateTime CreatedAt,
    bool Replayed);

public record StatementLine(
    long TransactionId,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Note,
    DateTime CreatedAt);
