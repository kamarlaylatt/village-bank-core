using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class UserService : IUserService
{
    private readonly BankDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<UserService> _logger;

    public UserService(
        BankDbContext db,
        IPasswordHasher<User> hasher,
        ICurrentUser currentUser,
        TimeProvider clock,
        ILogger<UserService> logger)
    {
        _db = db;
        _hasher = hasher;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<UserDetails> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var username = UsernamePolicy.NormalizeAndValidate(request.Username);

        if (!Enum.IsDefined(request.Role))
            throw new BusinessRuleException("Unknown role.");
        if (request.Role == UserRole.SuperAdmin)
            throw new BusinessRuleException("SuperAdmin accounts cannot be created through the API.");

        PasswordPolicy.Validate(request.TemporaryPassword, username);

        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            throw new BusinessRuleException("Username already exists.");

        var user = new User
        {
            Username = username,
            Role = request.Role,
            IsActive = true,
            MustChangePassword = true,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = _hasher.HashPassword(user, request.TemporaryPassword);

        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new BusinessRuleException("Username already exists.");
        }

        _logger.LogInformation(
            "User {ActorId} created user {UserId} ({Username}) with role {Role}.",
            _currentUser.UserId, user.Id, user.Username, user.Role);

        return ToDetails(user);
    }

    public async Task<IReadOnlyList<UserDetails>> ListAsync(CancellationToken ct = default)
    {
        return await _db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserDetails(
                u.Id, u.Username, u.Role, u.IsActive, u.MustChangePassword, u.CreatedAt, u.LastLoginAt))
            .ToListAsync(ct);
    }

    public async Task<UserDetails> ChangeRoleAsync(long id, UserRole role, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(role))
            throw new BusinessRuleException("Unknown role.");
        if (role == UserRole.SuperAdmin)
            throw new BusinessRuleException("Users cannot be promoted to SuperAdmin.");

        var user = await FindAsync(id, ct);

        if (user.Role == UserRole.SuperAdmin)
            throw new BusinessRuleException("The SuperAdmin role cannot be changed.");

        user.Role = role;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "User {ActorId} changed the role of user {UserId} to {Role}.", _currentUser.UserId, user.Id, role);

        return ToDetails(user);
    }

    public async Task<UserDetails> SetActiveAsync(long id, bool isActive, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);

        if (!isActive && user.Role == UserRole.SuperAdmin)
            throw new BusinessRuleException("A SuperAdmin account cannot be deactivated.");

        user.IsActive = isActive;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "User {ActorId} set user {UserId} active={IsActive}.", _currentUser.UserId, user.Id, isActive);

        return ToDetails(user);
    }

    public async Task ResetPasswordAsync(long id, string temporaryPassword, CancellationToken ct = default)
    {
        var user = await FindAsync(id, ct);

        if (user.Role == UserRole.SuperAdmin)
            throw new BusinessRuleException("A SuperAdmin changes their own password.");

        PasswordPolicy.Validate(temporaryPassword, user.Username);

        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        user.MustChangePassword = true;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {ActorId} reset the password of user {UserId}.", _currentUser.UserId, user.Id);
    }

    private async Task<User> FindAsync(long id, CancellationToken ct) =>
        await _db.Users.SingleOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException($"User {id} was not found.");

    private static UserDetails ToDetails(User u) =>
        new(u.Id, u.Username, u.Role, u.IsActive, u.MustChangePassword, u.CreatedAt, u.LastLoginAt);
}
