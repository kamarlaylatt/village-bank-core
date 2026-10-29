using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.SuperAdmin)]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;

    public UsersController(IUserService users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDetails>>> List(CancellationToken ct)
    {
        return Ok(await _users.ListAsync(ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var user = await _users.CreateAsync(request, ct);
        return Created($"/api/users/{user.Id}", user);
    }

    [HttpPut("{id:long}/role")]
    public async Task<ActionResult<UserDetails>> ChangeRole(long id, ChangeRoleRequest request, CancellationToken ct)
    {
        return await _users.ChangeRoleAsync(id, request.Role, ct);
    }

    [HttpPost("{id:long}/deactivate")]
    public async Task<ActionResult<UserDetails>> Deactivate(long id, CancellationToken ct)
    {
        return await _users.SetActiveAsync(id, false, ct);
    }

    [HttpPost("{id:long}/activate")]
    public async Task<ActionResult<UserDetails>> Activate(long id, CancellationToken ct)
    {
        return await _users.SetActiveAsync(id, true, ct);
    }

    [HttpPost("{id:long}/reset-password")]
    public async Task<IActionResult> ResetPassword(long id, ResetPasswordRequest request, CancellationToken ct)
    {
        await _users.ResetPasswordAsync(id, request.TemporaryPassword, ct);
        return NoContent();
    }
}
