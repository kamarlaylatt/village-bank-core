using VillageBank.Api.Domain;

namespace VillageBank.Api.Contracts;

public record ApplyLoanRequest(
    long MemberId,
    decimal Principal,
    decimal MonthlyInterestRatePercent,
    int TermMonths,
    string? Purpose);

public record RejectLoanRequest(string Reason);

public record RepayLoanRequest(decimal Amount, string? Note);

public record LoanPreviewRequest(decimal Principal, decimal MonthlyInterestRatePercent, int TermMonths);

public record InstallmentPreview(int Number, DateOnly DueDate, decimal Principal, decimal Interest, decimal Total);

public record LoanPreview(
    decimal Principal,
    decimal TotalInterest,
    decimal TotalRepayable,
    IReadOnlyList<InstallmentPreview> Installments);

public record InstallmentDetails(
    int Number,
    DateOnly DueDate,
    decimal PrincipalDue,
    decimal InterestDue,
    decimal PrincipalPaid,
    decimal InterestPaid,
    bool IsPaid);

public record LoanDetails(
    long Id,
    long MemberId,
    string MemberName,
    decimal Principal,
    decimal MonthlyInterestRatePercent,
    int TermMonths,
    string? Purpose,
    LoanStatus Status,
    DateTime AppliedAt,
    DateTime? DecidedAt,
    string? RejectionReason,
    DateOnly? DisbursedOn,
    decimal TotalInterest,
    decimal TotalPaid,
    decimal Outstanding,
    decimal OverdueAmount,
    int DaysPastDue,
    IReadOnlyList<InstallmentDetails> Installments,
    string? AppliedBy,
    string? DecidedBy,
    string? DisbursedBy);

public record LoanSummary(
    long Id,
    long MemberId,
    string MemberName,
    decimal Principal,
    LoanStatus Status,
    DateTime AppliedAt,
    DateOnly? DisbursedOn);

public record RepaymentResult(
    long RepaymentId,
    long LoanId,
    decimal Amount,
    decimal PrincipalPart,
    decimal InterestPart,
    decimal Outstanding,
    LoanStatus LoanStatus,
    long JournalEntryId,
    DateTime CreatedAt,
    bool Replayed);
