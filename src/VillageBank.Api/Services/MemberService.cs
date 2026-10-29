using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Services;

public class MemberService : IMemberService
{
    private static readonly Expression<Func<Member, MemberDetails>> ToDetails = m => new MemberDetails(
        m.Id,
        m.FullName,
        m.Phone,
        m.Village,
        m.JoinedOn,
        m.IsActive,
        m.SavingsAccount!.Id,
        m.SavingsAccount.Balance);

    private readonly BankDbContext _db;
    private readonly TimeProvider _clock;

    public MemberService(BankDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<MemberDetails> CreateAsync(CreateMemberRequest request, CancellationToken ct = default)
    {
        var fullName = Clean(request.FullName);
        if (fullName is null)
            throw new BusinessRuleException("Full name is required.");
        if (fullName.Length > 200)
            throw new BusinessRuleException("Full name can be at most 200 characters.");

        var phone = Clean(request.Phone);
        if (phone is { Length: > 30 })
            throw new BusinessRuleException("Phone can be at most 30 characters.");

        var village = Clean(request.Village);
        if (village is { Length: > 100 })
            throw new BusinessRuleException("Village can be at most 100 characters.");

        var now = _clock.GetUtcNow().UtcDateTime;

        var member = new Member
        {
            FullName = fullName,
            Phone = phone,
            Village = village,
            JoinedOn = BusinessTime.Today(_clock),
            CreatedAt = now,
            SavingsAccount = new SavingsAccount { OpenedAt = now }
        };

        _db.Members.Add(member);
        await _db.SaveChangesAsync(ct);

        return await GetAsync(member.Id, ct);
    }

    public async Task<MemberDetails> GetAsync(long id, CancellationToken ct = default)
    {
        return await _db.Members
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(ToDetails)
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Member {id} was not found.");
    }

    public async Task<PagedResult<MemberDetails>> ListAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Members.AsNoTracking();

        var term = Clean(search);
        if (term is not null)
        {
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(m =>
                EF.Functions.ILike(m.FullName, pattern) ||
                (m.Phone != null && EF.Functions.ILike(m.Phone, pattern)));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderBy(m => m.FullName).ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDetails)
            .ToListAsync(ct);

        return new PagedResult<MemberDetails>(items, page, pageSize, total);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
