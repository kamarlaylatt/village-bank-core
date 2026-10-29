using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Auth;
using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

[ApiController]
[Route("api/loans")]
[Authorize(Roles = Roles.Staff)]
public class LoansController : ControllerBase
{
    private readonly ILoanService _loans;

    public LoansController(ILoanService loans)
    {
        _loans = loans;
    }

    [HttpPost("preview")]
    public ActionResult<LoanPreview> Preview(LoanPreviewRequest request)
    {
        return _loans.Preview(request);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Operators)]
    public async Task<IActionResult> Apply(ApplyLoanRequest request, CancellationToken ct)
    {
        var loan = await _loans.ApplyAsync(request, ct);
        return Created($"/api/loans/{loan.Id}", loan);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<LoanSummary>>> List(
        [FromQuery] long? memberId,
        [FromQuery] LoanStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        return await _loans.ListAsync(memberId, status, page, pageSize, ct);
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<LoanDetails>> GetById(long id, CancellationToken ct)
    {
        return await _loans.GetAsync(id, ct);
    }

    [HttpPost("{id:long}/approve")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<ActionResult<LoanDetails>> Approve(long id, CancellationToken ct)
    {
        return await _loans.ApproveAsync(id, ct);
    }

    [HttpPost("{id:long}/reject")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<ActionResult<LoanDetails>> Reject(long id, RejectLoanRequest request, CancellationToken ct)
    {
        return await _loans.RejectAsync(id, request.Reason, ct);
    }

    [HttpPost("{id:long}/disburse")]
    [Authorize(Roles = Roles.Operators)]
    public async Task<ActionResult<LoanDetails>> Disburse(long id, CancellationToken ct)
    {
        return await _loans.DisburseAsync(id, ct);
    }

    [HttpPost("{id:long}/repayments")]
    [Authorize(Roles = Roles.Operators)]
    public async Task<IActionResult> Repay(
        long id,
        RepayLoanRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _loans.RepayAsync(id, request.Amount, request.Note, idempotencyKey, ct);

        return result.Replayed
            ? Ok(result)
            : Created($"/api/loans/{id}", result);
    }
}
