using VillageBank.Api.Auth;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Demo;

// The seeder runs outside an HTTP request, so there is no token to read the user from.
public sealed class FixedCurrentUser : ICurrentUser
{
    public FixedCurrentUser(long? userId, UserRole? role)
    {
        UserId = userId;
        Role = role;
    }

    public long? UserId { get; }
    public UserRole? Role { get; }
}
