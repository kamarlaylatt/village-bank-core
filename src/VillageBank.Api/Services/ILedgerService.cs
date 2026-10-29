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

    // Validates and adds the entry to the DbContext without saving, so the caller can
    // commit it together with its own changes in a single SaveChanges.
    Task<JournalEntry> StageAsync(
        DateOnly entryDate,
        string description,
        IReadOnlyList<LineInput> lines,
        string? sourceType = null,
        long? sourceId = null,
        CancellationToken ct = default);
}
