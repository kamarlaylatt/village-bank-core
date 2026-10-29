using VillageBank.Api.Contracts;

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
}
