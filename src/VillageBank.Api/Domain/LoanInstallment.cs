namespace VillageBank.Api.Domain;

public class LoanInstallment
{
    public long Id { get; set; }

    public long LoanId { get; set; }
    public Loan Loan { get; set; } = null!;

    public int Number { get; set; }
    public DateOnly DueDate { get; set; }

    public decimal PrincipalDue { get; set; }
    public decimal InterestDue { get; set; }
    public decimal PrincipalPaid { get; set; }
    public decimal InterestPaid { get; set; }

    public decimal TotalDue => PrincipalDue + InterestDue;
    public decimal TotalPaid => PrincipalPaid + InterestPaid;
    public bool IsPaid => TotalPaid >= TotalDue;
}
