using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

[ApiController]
[Route("api/members")]
[Authorize(Roles = Roles.Staff)]
public class MembersController : ControllerBase
{
    private readonly IMemberService _members;

    public MembersController(IMemberService members)
    {
        _members = members;
    }

    [HttpPost]
    [Authorize(Roles = Roles.Operators)]
    public async Task<IActionResult> Create(CreateMemberRequest request, CancellationToken ct)
    {
        var member = await _members.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = member.Id }, member);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<MemberDetails>> GetById(long id, CancellationToken ct)
    {
        return await _members.GetAsync(id, ct);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<MemberDetails>>> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _members.ListAsync(search, page, pageSize, ct);
    }
}
