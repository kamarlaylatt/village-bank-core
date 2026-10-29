namespace VillageBank.Api.Contracts;

public record CreateMemberRequest(string FullName, string? Phone, string? Village);

public record MemberDetails(
    long Id,
    string FullName,
    string? Phone,
    string? Village,
    DateOnly JoinedOn,
    bool IsActive,
    long SavingsAccountId,
    decimal SavingsBalance);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
