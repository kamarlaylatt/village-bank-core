using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public interface ILedgerService
{
    Task<JournalEntry> PostAsync(
        DateOnly entryDate,
        string description,
        IReadOnlyList<LineInput> lines,
        string? sourceType = null,
        long? sourceId = null,
        CancellationToken ct = default);
}
