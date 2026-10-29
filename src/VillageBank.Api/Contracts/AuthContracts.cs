using VillageBank.Api.Domain;

namespace VillageBank.Api.Contracts;

public record LoginRequest(string Username, string Password);

public record LoginResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string Username,
    UserRole Role,
    bool MustChangePassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
