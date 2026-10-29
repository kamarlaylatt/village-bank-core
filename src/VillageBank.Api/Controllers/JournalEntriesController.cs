using Microsoft.AspNetCore.Mvc;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Api.Controllers;

public record PostJournalEntryRequest(DateOnly EntryDate, string Description, List<LineInput> Lines);

[ApiController]
[Route("api/journal-entries")]
public class JournalEntriesController : ControllerBase
{
    private readonly ILedgerService _ledger;

    public JournalEntriesController(ILedgerService ledger)
    {
        _ledger = ledger;
    }

    [HttpPost]
    public async Task<IActionResult> Post(PostJournalEntryRequest request, CancellationToken ct)
    {
        var entry = await _ledger.PostAsync(
            request.EntryDate, request.Description, request.Lines, ct: ct);

        return Created($"/api/journal-entries/{entry.Id}", new { entry.Id });
    }
}
