using VillageBank.Api.Contracts;

namespace VillageBank.Api.Services;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<LoginResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
}
