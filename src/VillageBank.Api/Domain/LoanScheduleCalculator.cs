namespace VillageBank.Api.Domain;

public record ScheduledInstallment(int Number, DateOnly DueDate, decimal Principal, decimal Interest);

public static class LoanRules
{
    public const decimal MaxPrincipal = 1_000_000_000m;
    public const decimal MaxMonthlyRatePercent = 10m;
    public const int MaxTermMonths = 60;

    public static void ValidateTerms(decimal principal, decimal monthlyRatePercent, int termMonths)
    {
        if (principal <= 0)
            throw new BusinessRuleException("Principal must be greater than zero.");
        if (principal > MaxPrincipal)
            throw new BusinessRuleException("Principal is too large.");
        if (principal != decimal.Truncate(principal))
            throw new BusinessRuleException("Principal must be a whole number of kip.");

        if (monthlyRatePercent < 0 || monthlyRatePercent > MaxMonthlyRatePercent)
            throw new BusinessRuleException($"Monthly interest rate must be between 0 and {MaxMonthlyRatePercent} percent.");
        if (decimal.Round(monthlyRatePercent, 4) != monthlyRatePercent)
            throw new BusinessRuleException("Monthly interest rate can have at most 4 decimal places.");

        if (termMonths < 1 || termMonths > MaxTermMonths)
            throw new BusinessRuleException($"Term must be between 1 and {MaxTermMonths} months.");
    }
}

public static class LoanScheduleCalculator
{
    // Flat rate: interest is charged on the original principal every month, so every
    // installment is the same size. Kip has no decimals, so the rounding remainder goes
    // to the last installment and the totals always add up exactly.
    public static decimal TotalInterest(decimal principal, decimal monthlyRatePercent, int termMonths) =>
        decimal.Round(principal * monthlyRatePercent / 100m * termMonths, 0, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<ScheduledInstallment> Calculate(
        decimal principal, decimal monthlyRatePercent, int termMonths, DateOnly disbursedOn)
    {
        LoanRules.ValidateTerms(principal, monthlyRatePercent, termMonths);

        var totalInterest = TotalInterest(principal, monthlyRatePercent, termMonths);
        var principalPerMonth = decimal.Truncate(principal / termMonths);
        var interestPerMonth = decimal.Truncate(totalInterest / termMonths);

        var installments = new List<ScheduledInstallment>(termMonths);
        var principalLeft = principal;
        var interestLeft = totalInterest;

        for (var number = 1; number <= termMonths; number++)
        {
            var isLast = number == termMonths;
            var principalPart = isLast ? principalLeft : principalPerMonth;
            var interestPart = isLast ? interestLeft : interestPerMonth;

            installments.Add(new ScheduledInstallment(
                number, disbursedOn.AddMonths(number), principalPart, interestPart));

            principalLeft -= principalPart;
            interestLeft -= interestPart;
        }

        return installments;
    }
}
