using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Contracts;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

[ApiController]
[Route("api/savings-accounts")]
public class SavingsAccountsController : ControllerBase
{
    private readonly ISavingsService _savings;

    public SavingsAccountsController(ISavingsService savings)
    {
        _savings = savings;
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<SavingsAccountSummary>> GetById(long id, CancellationToken ct)
    {
        return await _savings.GetAccountAsync(id, ct);
    }

    [HttpGet("{id:long}/statement")]
    public async Task<ActionResult<IReadOnlyList<StatementLine>>> GetStatement(
        long id, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        return Ok(await _savings.GetStatementAsync(id, take, ct));
    }

    [HttpPost("{id:long}/deposits")]
    public async Task<IActionResult> Deposit(
        long id,
        MoneyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _savings.DepositAsync(id, request.Amount, request.Note, idempotencyKey, ct);
        return ToResponse(id, result);
    }

    [HttpPost("{id:long}/withdrawals")]
    public async Task<IActionResult> Withdraw(
        long id,
        MoneyRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await _savings.WithdrawAsync(id, request.Amount, request.Note, idempotencyKey, ct);
        return ToResponse(id, result);
    }

    private IActionResult ToResponse(long accountId, SavingsTransactionResult result) =>
        result.Replayed
            ? Ok(result)
            : Created($"/api/savings-accounts/{accountId}/statement", result);
}
