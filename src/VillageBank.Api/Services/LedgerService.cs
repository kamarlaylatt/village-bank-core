using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Auth;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class LedgerService : ILedgerService
{
    private readonly BankDbContext _db;
    private readonly ICurrentUser _currentUser;

    public LedgerService(BankDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<JournalEntry> PostAsync(
        DateOnly entryDate,
        string description,
        IReadOnlyList<LineInput> lines,
        string? sourceType = null,
        long? sourceId = null,
        CancellationToken ct = default)
    {
        var entry = await StageAsync(entryDate, description, lines, sourceType, sourceId, ct);
        await _db.SaveChangesAsync(ct);

        return entry;
    }

    public async Task<JournalEntry> StageAsync(
        DateOnly entryDate,
        string description,
        IReadOnlyList<LineInput> lines,
        string? sourceType = null,
        long? sourceId = null,
        CancellationToken ct = default)
    {
        JournalEntryValidator.Validate(description, lines);

        var accountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        var activeCount = await _db.Accounts
            .CountAsync(a => accountIds.Contains(a.Id) && a.IsActive, ct);

        if (activeCount != accountIds.Count)
            throw new LedgerException("One or more accounts do not exist or are inactive.");

        var entry = new JournalEntry
        {
            EntryDate = entryDate,
            Description = description.Trim(),
            SourceType = sourceType,
            SourceId = sourceId,
            CreatedByUserId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow,
            Lines = lines.Select(l => new JournalLine
            {
                AccountId = l.AccountId,
                Debit = l.Debit,
                Credit = l.Credit
            }).ToList()
        };

        _db.JournalEntries.Add(entry);

        return entry;
    }
}
