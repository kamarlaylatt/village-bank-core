using VillageBank.Api.Domain;

namespace VillageBank.Tests;

public class LoanScheduleCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 30);

    [Fact]
    public void A_flat_rate_loan_has_equal_installments()
    {
        // 300,000 kip over 10 months at 2% per month on the original principal.
        var schedule = LoanScheduleCalculator.Calculate(300_000m, 2m, 10, Start);

        Assert.Equal(10, schedule.Count);
        Assert.All(schedule, s =>
        {
            Assert.Equal(30_000m, s.Principal);
            Assert.Equal(6_000m, s.Interest);
        });
        Assert.Equal(300_000m, schedule.Sum(s => s.Principal));
        Assert.Equal(60_000m, schedule.Sum(s => s.Interest));
    }

    [Fact]
    public void The_principal_remainder_goes_to_the_last_installment()
    {
        var schedule = LoanScheduleCalculator.Calculate(100_000m, 2m, 3, Start);

        Assert.Equal(new[] { 33_333m, 33_333m, 33_334m }, schedule.Select(s => s.Principal));
        Assert.Equal(100_000m, schedule.Sum(s => s.Principal));
    }

    [Fact]
    public void The_interest_remainder_goes_to_the_last_installment()
    {
        // 100,000 x 1.3333% x 3 = 3,999.9, rounded to 4,000 and split 1,333 / 1,333 / 1,334.
        var schedule = LoanScheduleCalculator.Calculate(100_000m, 1.3333m, 3, Start);

        Assert.Equal(new[] { 1_333m, 1_333m, 1_334m }, schedule.Select(s => s.Interest));
        Assert.Equal(4_000m, schedule.Sum(s => s.Interest));
    }

    [Fact]
    public void Due_dates_fall_monthly_from_the_disbursement_date()
    {
        var schedule = LoanScheduleCalculator.Calculate(120_000m, 2m, 3, new DateOnly(2027, 1, 31));

        Assert.Equal(
            new[] { new DateOnly(2027, 2, 28), new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 30) },
            schedule.Select(s => s.DueDate));
    }

    [Fact]
    public void A_zero_interest_loan_has_no_interest()
    {
        var schedule = LoanScheduleCalculator.Calculate(60_000m, 0m, 6, Start);

        Assert.All(schedule, s => Assert.Equal(0m, s.Interest));
        Assert.Equal(60_000m, schedule.Sum(s => s.Principal));
    }

    [Theory]
    [InlineData(0.0, 2.0, 10)]
    [InlineData(-1000.0, 2.0, 10)]
    [InlineData(1000.5, 2.0, 10)]
    [InlineData(1000.0, -1.0, 10)]
    [InlineData(1000.0, 11.0, 10)]
    [InlineData(1000.0, 2.0, 0)]
    [InlineData(1000.0, 2.0, 61)]
    public void Invalid_terms_are_rejected(double principal, double ratePercent, int termMonths)
    {
        Assert.Throws<BusinessRuleException>(
            () => LoanScheduleCalculator.Calculate((decimal)principal, (decimal)ratePercent, termMonths, Start));
    }
}
