using VillageBank.Api.Domain;

namespace VillageBank.Api.Auth;

public interface ICurrentUser
{
    long? UserId { get; }
    UserRole? Role { get; }
}
