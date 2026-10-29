using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Contracts;
using VillageBank.Api.Data;
using VillageBank.Api.Domain;
using VillageBank.Api.Services;

namespace VillageBank.Tests;

[Collection("postgres")]
public class LoanServiceTests
{
    private readonly PostgresFixture _fixture;

    public LoanServiceTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Applying_creates_a_pending_loan_without_a_schedule()
    {
        var memberId = await CreateMemberAsync();

        var loan = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));

        Assert.Equal(LoanStatus.Pending, loan.Status);
        Assert.Empty(loan.Installments);
        Assert.Equal(60_000m, loan.TotalInterest);
    }

    [Fact]
    public async Task A_member_cannot_have_two_loans_in_progress_until_the_first_is_rejected()
    {
        var manager = await CreateUserAsync(UserRole.Manager);
        var memberId = await CreateMemberAsync();
        var first = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(null, s => s.ApplyAsync(Terms(memberId))));

        await AsAsync(manager, s => s.RejectAsync(first.Id, "not enough savings"));
        var second = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));

        Assert.Equal(LoanStatus.Pending, second.Status);
    }

    [Fact]
    public async Task The_applicant_cannot_approve_their_own_loan()
    {
        var applicant = await CreateUserAsync(UserRole.Manager);
        var other = await CreateUserAsync(UserRole.Manager);
        var memberId = await CreateMemberAsync();
        var loan = await AsAsync(applicant, s => s.ApplyAsync(Terms(memberId)));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(applicant, s => s.ApproveAsync(loan.Id)));
        Assert.Contains("applied", ex.Message);

        var approved = await AsAsync(other, s => s.ApproveAsync(loan.Id));
        Assert.Equal(LoanStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Only_pending_loans_can_be_approved_or_rejected()
    {
        var manager = await CreateUserAsync(UserRole.Manager);
        var memberId = await CreateMemberAsync();
        var loan = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));
        await AsAsync(manager, s => s.ApproveAsync(loan.Id));

        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(manager, s => s.ApproveAsync(loan.Id)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(manager, s => s.RejectAsync(loan.Id, "too late")));
    }

    [Fact]
    public async Task Rejecting_a_loan_needs_a_reason()
    {
        var manager = await CreateUserAsync(UserRole.Manager);
        var memberId = await CreateMemberAsync();
        var loan = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(manager, s => s.RejectAsync(loan.Id, "  ")));

        var rejected = await AsAsync(manager, s => s.RejectAsync(loan.Id, "no guarantor"));
        Assert.Equal(LoanStatus.Rejected, rejected.Status);
        Assert.Equal("no guarantor", rejected.RejectionReason);
    }

    [Fact]
    public async Task A_loan_must_be_approved_before_it_is_disbursed()
    {
        var memberId = await CreateMemberAsync();
        var loan = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(null, s => s.DisburseAsync(loan.Id)));
    }

    [Fact]
    public async Task Disbursement_creates_the_schedule_and_posts_a_ledger_entry()
    {
        var loanId = await ActiveLoanAsync();

        var loan = await AsAsync(null, s => s.GetAsync(loanId));

        Assert.Equal(LoanStatus.Active, loan.Status);
        Assert.Equal(10, loan.Installments.Count);
        Assert.Equal(360_000m, loan.Outstanding);
        Assert.Equal(loan.DisbursedOn!.Value.AddMonths(1), loan.Installments[0].DueDate);

        await using var db = _fixture.CreateDbContext();
        var entryId = (await db.Loans.AsNoTracking().SingleAsync(l => l.Id == loanId)).DisbursementJournalEntryId!.Value;
        var lines = await LinesAsync(entryId);

        Assert.Equal(2, lines.Count);
        Assert.Equal(300_000m, lines.Single(l => l.Account.Code == AccountCodes.LoansReceivable).Debit);
        Assert.Equal(300_000m, lines.Single(l => l.Account.Code == AccountCodes.Cash).Credit);
    }

    [Fact]
    public async Task A_repayment_pays_interest_first_then_principal_and_posts_a_balanced_entry()
    {
        var loanId = await ActiveLoanAsync();

        var result = await AsAsync(null, s => s.RepayAsync(loanId, 10_000m, null, null));

        Assert.Equal(6_000m, result.InterestPart);
        Assert.Equal(4_000m, result.PrincipalPart);
        Assert.Equal(350_000m, result.Outstanding);

        var loan = await AsAsync(null, s => s.GetAsync(loanId));
        Assert.Equal(6_000m, loan.Installments[0].InterestPaid);
        Assert.Equal(4_000m, loan.Installments[0].PrincipalPaid);

        var lines = await LinesAsync(result.JournalEntryId);
        Assert.Equal(3, lines.Count);
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
        Assert.Equal(10_000m, lines.Single(l => l.Account.Code == AccountCodes.Cash).Debit);
        Assert.Equal(4_000m, lines.Single(l => l.Account.Code == AccountCodes.LoansReceivable).Credit);
        Assert.Equal(6_000m, lines.Single(l => l.Account.Code == AccountCodes.InterestIncome).Credit);
    }

    [Fact]
    public async Task A_large_repayment_spills_over_into_the_next_installment()
    {
        var loanId = await ActiveLoanAsync();

        await AsAsync(null, s => s.RepayAsync(loanId, 40_000m, null, null));

        var loan = await AsAsync(null, s => s.GetAsync(loanId));
        Assert.True(loan.Installments[0].IsPaid);
        Assert.Equal(4_000m, loan.Installments[1].InterestPaid);
        Assert.Equal(0m, loan.Installments[1].PrincipalPaid);
    }

    [Fact]
    public async Task Paying_the_full_balance_closes_the_loan()
    {
        var loanId = await ActiveLoanAsync();

        var result = await AsAsync(null, s => s.RepayAsync(loanId, 360_000m, null, null));

        Assert.Equal(LoanStatus.Closed, result.LoanStatus);
        Assert.Equal(0m, result.Outstanding);
        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(null, s => s.RepayAsync(loanId, 1_000m, null, null)));
    }

    [Fact]
    public async Task Paying_more_than_the_outstanding_balance_is_rejected()
    {
        var loanId = await ActiveLoanAsync();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => AsAsync(null, s => s.RepayAsync(loanId, 360_001m, null, null)));

        Assert.Contains("exceeds", ex.Message);
    }

    [Fact]
    public async Task A_loan_that_is_not_active_cannot_be_repaid()
    {
        var memberId = await CreateMemberAsync();
        var loan = await AsAsync(null, s => s.ApplyAsync(Terms(memberId)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => AsAsync(null, s => s.RepayAsync(loan.Id, 1_000m, null, null)));
    }

    [Fact]
    public async Task Repeating_a_repayment_with_the_same_idempotency_key_does_not_pay_twice()
    {
        var loanId = await ActiveLoanAsync();

        var first = await AsAsync(null, s => s.RepayAsync(loanId, 10_000m, null, "pay-1"));
        var second = await AsAsync(null, s => s.RepayAsync(loanId, 10_000m, null, "pay-1"));

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.RepaymentId, second.RepaymentId);

        var loan = await AsAsync(null, s => s.GetAsync(loanId));
        Assert.Equal(350_000m, loan.Outstanding);
    }

    [Fact]
    public async Task Concurrent_full_repayments_succeed_only_once()
    {
        var loanId = await ActiveLoanAsync();

        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            try
            {
                await AsAsync(null, s => s.RepayAsync(loanId, 360_000m, null, null));
                return true;
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        });

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(succeeded => succeeded));

        var loan = await AsAsync(null, s => s.GetAsync(loanId));
        Assert.Equal(LoanStatus.Closed, loan.Status);
        Assert.Equal(360_000m, loan.TotalPaid);
    }

    [Fact]
    public async Task Loans_receivable_in_the_ledger_matches_the_principal_still_owed()
    {
        var loanId = await ActiveLoanAsync();
        await AsAsync(null, s => s.RepayAsync(loanId, 40_000m, null, null));
        await AsAsync(null, s => s.RepayAsync(loanId, 25_000m, null, null));

        await using var db = _fixture.CreateDbContext();

        var entryIds = await db.LoanRepayments
            .Where(r => r.LoanId == loanId)
            .Select(r => r.JournalEntryId)
            .ToListAsync();
        entryIds.Add((await db.Loans.AsNoTracking().SingleAsync(l => l.Id == loanId)).DisbursementJournalEntryId!.Value);

        var ledgerBalance = await db.JournalLines
            .Where(l => entryIds.Contains(l.JournalEntryId) && l.Account.Code == AccountCodes.LoansReceivable)
            .SumAsync(l => l.Debit - l.Credit);

        var loan = await AsAsync(null, s => s.GetAsync(loanId));
        var principalPaid = loan.Installments.Sum(i => i.PrincipalPaid);

        Assert.Equal(300_000m - principalPaid, ledgerBalance);
    }

    private static ApplyLoanRequest Terms(long memberId) => new(memberId, 300_000m, 2m, 10, "test");

    private async Task<T> AsAsync<T>(long? userId, Func<LoanService, Task<T>> action)
    {
        await using var db = _fixture.CreateDbContext();
        var user = new TestCurrentUser { UserId = userId };

        return await action(new LoanService(db, new LedgerService(db, user), TimeProvider.System, user));
    }

    private async Task<long> ActiveLoanAsync()
    {
        var teller = await CreateUserAsync(UserRole.Teller);
        var manager = await CreateUserAsync(UserRole.Manager);
        var memberId = await CreateMemberAsync();

        var loan = await AsAsync(teller, s => s.ApplyAsync(Terms(memberId)));
        await AsAsync(manager, s => s.ApproveAsync(loan.Id));
        await AsAsync(teller, s => s.DisburseAsync(loan.Id));

        return loan.Id;
    }

    private async Task<long> CreateMemberAsync()
    {
        await using var db = _fixture.CreateDbContext();

        var member = await new MemberService(db, TimeProvider.System)
            .CreateAsync(new CreateMemberRequest($"Loan Member {Guid.NewGuid():N}", null, null));

        return member.Id;
    }

    // Loans record who acted, and that is a foreign key to users, so the tests need real user rows.
    private async Task<long> CreateUserAsync(UserRole role)
    {
        await using var db = _fixture.CreateDbContext();

        var user = new User
        {
            Username = $"{role.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..20],
            PasswordHash = "not-a-real-hash",
            Role = role,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    private async Task<List<JournalLine>> LinesAsync(long journalEntryId)
    {
        await using var db = _fixture.CreateDbContext();

        return await db.JournalLines
            .AsNoTracking()
            .Include(l => l.Account)
            .Where(l => l.JournalEntryId == journalEntryId)
            .ToListAsync();
    }
}
