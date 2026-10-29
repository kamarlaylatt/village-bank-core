using VillageBank.Api.Domain;

namespace VillageBank.Api.Contracts;

public record CreateUserRequest(string Username, UserRole Role, string TemporaryPassword);

public record ChangeRoleRequest(UserRole Role);

public record ResetPasswordRequest(string TemporaryPassword);

public record UserDetails(
    long Id,
    string Username,
    UserRole Role,
    bool IsActive,
    bool MustChangePassword,
    DateTime CreatedAt,
    DateTime? LastLoginAt);
