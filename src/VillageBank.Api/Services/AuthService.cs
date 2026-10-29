using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class AuthService : IAuthService
{
    private const string InvalidCredentials = "Invalid username or password.";

    // Verified when the username does not exist, so an unknown username takes as long to reject
    // as a wrong password and cannot be told apart by response time.
    private static readonly User DummyUser = new() { Username = "dummy" };
    private static readonly string DummyHash =
        new PasswordHasher<User>().HashPassword(DummyUser, "dummy-password-for-timing");

    private readonly BankDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly IJwtTokenService _tokens;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public AuthService(
        BankDbContext db,
        IPasswordHasher<User> hasher,
        IJwtTokenService tokens,
        ICurrentUser currentUser,
        TimeProvider clock)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);

        if (user is null)
        {
            _hasher.VerifyHashedPassword(DummyUser, DummyHash, request.Password);
            throw new UnauthorizedException(InvalidCredentials);
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (result == PasswordVerificationResult.Failed || !user.IsActive)
            throw new UnauthorizedException(InvalidCredentials);

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = _hasher.HashPassword(user, request.Password);

        user.LastLoginAt = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync(ct);

        return ToResponse(user);
    }

    public async Task<LoginResponse> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException("You are not signed in.");

        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new UnauthorizedException("You are not signed in.");

        if (_hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new BusinessRuleException("The current password is incorrect.");

        PasswordPolicy.Validate(request.NewPassword, user.Username);

        if (request.NewPassword == request.CurrentPassword)
            throw new BusinessRuleException("The new password must be different from the current one.");

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync(ct);

        return ToResponse(user);
    }

    private LoginResponse ToResponse(User user)
    {
        var (token, expiresAt) = _tokens.CreateToken(user);
        return new LoginResponse(token, expiresAt, user.Username, user.Role, user.MustChangePassword);
    }
}
