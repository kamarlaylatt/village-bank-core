using VillageBank.Api.Contracts;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Tests;

[Collection("postgres")]
public class MemberServiceTests
{
    private readonly PostgresFixture _fixture;

    public MemberServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Create_opens_a_savings_account_with_zero_balance()
    {
        await using var db = _fixture.CreateDbContext();
        var members = new MemberService(db, TimeProvider.System);

        var member = await members.CreateAsync(new CreateMemberRequest("  Ma Aye  ", "020 5555 0000", "Ban Nong"));

        Assert.Equal("Ma Aye", member.FullName);
        Assert.Equal(0m, member.SavingsBalance);
        Assert.True(member.SavingsAccountId > 0);
        Assert.True(member.IsActive);
    }

    [Fact]
    public async Task Create_rejects_a_blank_name()
    {
        await using var db = _fixture.CreateDbContext();
        var members = new MemberService(db, TimeProvider.System);

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => members.CreateAsync(new CreateMemberRequest("   ", null, null)));
    }

    [Fact]
    public async Task Get_unknown_member_throws_not_found()
    {
        await using var db = _fixture.CreateDbContext();
        var members = new MemberService(db, TimeProvider.System);

        await Assert.ThrowsAsync<NotFoundException>(() => members.GetAsync(long.MaxValue));
    }

    [Fact]
    public async Task List_filters_by_name_case_insensitively()
    {
        var token = Guid.NewGuid().ToString("N");

        await using var db = _fixture.CreateDbContext();
        var members = new MemberService(db, TimeProvider.System);

        await members.CreateAsync(new CreateMemberRequest($"Ma Aye {token}", null, null));
        await members.CreateAsync(new CreateMemberRequest($"Ko Maung {token}", null, null));

        var result = await members.ListAsync($"MA AYE {token}", page: 1, pageSize: 20);

        Assert.Equal(1, result.Total);
        Assert.Equal($"Ma Aye {token}", Assert.Single(result.Items).FullName);
    }
}
