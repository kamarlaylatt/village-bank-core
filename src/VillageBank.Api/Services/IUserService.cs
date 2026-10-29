using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public interface IUserService
{
    Task<UserDetails> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<UserDetails>> ListAsync(CancellationToken ct = default);

    Task<UserDetails> ChangeRoleAsync(long id, UserRole role, CancellationToken ct = default);

    Task<UserDetails> SetActiveAsync(long id, bool isActive, CancellationToken ct = default);

    Task ResetPasswordAsync(long id, string temporaryPassword, CancellationToken ct = default);
}
