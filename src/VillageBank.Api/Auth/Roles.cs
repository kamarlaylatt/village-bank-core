using VillageBank.Api.Domain;

namespace VillageBank.Api.Auth;

public static class Roles
{
    public const string SuperAdmin = nameof(UserRole.SuperAdmin);
    public const string Manager = nameof(UserRole.Manager);
    public const string Teller = nameof(UserRole.Teller);
    public const string Auditor = nameof(UserRole.Auditor);

    public const string Operators = Teller + "," + Manager;
    public const string Reporters = Manager + "," + Auditor;
    public const string Staff = Teller + "," + Manager + "," + Auditor;
}
