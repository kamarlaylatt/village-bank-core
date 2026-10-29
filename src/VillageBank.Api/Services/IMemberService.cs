using VillageBank.Api.Contracts;

namespace VillageBank.Api.Services;

public interface IMemberService
{
    Task<MemberDetails> CreateAsync(CreateMemberRequest request, CancellationToken ct = default);

    Task<MemberDetails> GetAsync(long id, CancellationToken ct = default);

    Task<PagedResult<MemberDetails>> ListAsync(
        string? search, int page, int pageSize, CancellationToken ct = default);
}
