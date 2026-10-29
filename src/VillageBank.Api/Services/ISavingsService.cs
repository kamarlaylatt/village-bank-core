using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public interface ISavingsService
{
    Task<SavingsAccountSummary> GetAccountAsync(long accountId, CancellationToken ct = default);

    Task<SavingsTransactionResult> DepositAsync(
        long accountId, decimal amount, string? note, string? idempotencyKey, CancellationToken ct = default);

    Task<SavingsTransactionResult> WithdrawAsync(
        long accountId, decimal amount, string? note, string? idempotencyKey, CancellationToken ct = default);

    Task<IReadOnlyList<StatementLine>> GetStatementAsync(
        long accountId, int take, CancellationToken ct = default);

    // Credits loan proceeds to the member's savings account. Unlike DepositAsync it does not open
    // or commit a transaction: the caller is already inside one and calls SaveChangesAsync itself.
    Task<SavingsTransaction> StageLoanProceedsAsync(
        long memberId, decimal amount, DateOnly entryDate, string description, CancellationToken ct = default);
}
