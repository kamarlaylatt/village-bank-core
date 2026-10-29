using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public interface ILoanService
{
    LoanPreview Preview(LoanPreviewRequest request);

    Task<LoanDetails> ApplyAsync(ApplyLoanRequest request, CancellationToken ct = default);

    Task<LoanDetails> GetAsync(long id, CancellationToken ct = default);

    Task<PagedResult<LoanSummary>> ListAsync(
        long? memberId, LoanStatus? status, int page, int pageSize, CancellationToken ct = default);

    Task<LoanDetails> ApproveAsync(long id, CancellationToken ct = default);

    Task<LoanDetails> RejectAsync(long id, string reason, CancellationToken ct = default);

    Task<LoanDetails> DisburseAsync(long id, CancellationToken ct = default);

    Task<RepaymentResult> RepayAsync(
        long id, decimal amount, string? note, string? idempotencyKey, CancellationToken ct = default);
}
