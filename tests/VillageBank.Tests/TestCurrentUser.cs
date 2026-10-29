using VillageBank.Api.Auth;
using VillageBank.Api.Domain;

namespace VillageBank.Tests;

public class TestCurrentUser : ICurrentUser
{
    public static readonly TestCurrentUser Anonymous = new();

    public long? UserId { get; init; }
    public UserRole? Role { get; init; }
}
