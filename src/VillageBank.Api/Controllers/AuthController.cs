using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        return await _auth.LoginAsync(request, ct);
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<ActionResult<LoginResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        return await _auth.ChangePasswordAsync(request, ct);
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            id = User.FindFirst(JwtClaims.Subject)?.Value,
            username = User.FindFirst(JwtClaims.Name)?.Value,
            role = User.FindFirst(JwtClaims.Role)?.Value
        });
    }
}
