using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Data;

public class BankDbContext : DbContext
{
    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<SavingsAccount> SavingsAccounts => Set<SavingsAccount>();
    public DbSet<SavingsTransaction> SavingsTransactions => Set<SavingsTransaction>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Loan> Loans => Set<Loan>();
    public DbSet<LoanInstallment> LoanInstallments => Set<LoanInstallment>();
    public DbSet<LoanRepayment> LoanRepayments => Set<LoanRepayment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(10).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_accounts_type",
                "type IN ('Asset','Liability','Equity','Income','Expense')"));

            e.HasData(
                new Account { Id = 1, Code = "1000", Name = "Cash", Type = AccountType.Asset },
                new Account { Id = 2, Code = "1200", Name = "Loans Receivable", Type = AccountType.Asset },
                new Account { Id = 3, Code = "2000", Name = "Member Savings", Type = AccountType.Liability },
                new Account { Id = 4, Code = "3000", Name = "Share Capital", Type = AccountType.Equity },
                new Account { Id = 5, Code = "4000", Name = "Interest Income", Type = AccountType.Income },
                new Account { Id = 6, Code = "4100", Name = "Penalty Income", Type = AccountType.Income },
                new Account { Id = 7, Code = "5000", Name = "Interest Expense on Savings", Type = AccountType.Expense });
        });

        modelBuilder.Entity<JournalEntry>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.HasIndex(x => new { x.SourceType, x.SourceId });
            e.HasIndex(x => x.EntryDate);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JournalLine>(e =>
        {
            e.Property(x => x.Debit).HasPrecision(19, 4);
            e.Property(x => x.Credit).HasPrecision(19, 4);

            e.HasOne(x => x.JournalEntry)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.AccountId);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_journal_lines_one_sided",
                "debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))"));
        });

        modelBuilder.Entity<Member>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Village).HasMaxLength(100);
        });

        modelBuilder.Entity<SavingsAccount>(e =>
        {
            e.Property(x => x.Balance).HasPrecision(19, 4);

            e.HasOne(x => x.Member)
                .WithOne(x => x.SavingsAccount)
                .HasForeignKey<SavingsAccount>(x => x.MemberId)
                .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_savings_accounts_balance_non_negative",
                "balance >= 0"));
        });

        modelBuilder.Entity<SavingsTransaction>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.BalanceAfter).HasPrecision(19, 4);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(500);

            e.HasOne(x => x.SavingsAccount)
                .WithMany()
                .HasForeignKey(x => x.SavingsAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.SavingsAccountId, x.IdempotencyKey }).IsUnique();

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_savings_transactions_amount_positive", "amount > 0");
                t.HasCheckConstraint("ck_savings_transactions_type", "type IN ('Deposit','Withdrawal')");
            });
        });

        modelBuilder.Entity<Loan>(e =>
        {
            e.Property(x => x.Principal).HasPrecision(19, 4);
            e.Property(x => x.MonthlyInterestRatePercent).HasPrecision(7, 4);
            e.Property(x => x.Purpose).HasMaxLength(200);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

            e.HasOne(x => x.Member)
                .WithMany()
                .HasForeignKey(x => x.MemberId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.DisbursementJournalEntry)
                .WithMany()
                .HasForeignKey(x => x.DisbursementJournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>().WithMany().HasForeignKey(x => x.AppliedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.DecidedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.DisbursedByUserId).OnDelete(DeleteBehavior.Restrict);

            // One loan in progress per member, enforced by the database as well as the service.
            e.HasIndex(x => x.MemberId)
                .IsUnique()
                .HasFilter("status NOT IN ('Rejected','Closed')");

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_loans_status",
                    "status IN ('Pending','Approved','Rejected','Active','Closed')");
                t.HasCheckConstraint(
                    "ck_loans_terms",
                    "principal > 0 AND term_months BETWEEN 1 AND 60 AND monthly_interest_rate_percent >= 0");
            });
        });

        modelBuilder.Entity<LoanInstallment>(e =>
        {
            e.Property(x => x.PrincipalDue).HasPrecision(19, 4);
            e.Property(x => x.InterestDue).HasPrecision(19, 4);
            e.Property(x => x.PrincipalPaid).HasPrecision(19, 4);
            e.Property(x => x.InterestPaid).HasPrecision(19, 4);

            e.HasOne(x => x.Loan)
                .WithMany(x => x.Installments)
                .HasForeignKey(x => x.LoanId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.LoanId, x.Number }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "ck_loan_installments_amounts",
                "principal_due >= 0 AND interest_due >= 0 AND principal_paid >= 0 AND interest_paid >= 0 " +
                "AND principal_paid <= principal_due AND interest_paid <= interest_due"));
        });

        modelBuilder.Entity<LoanRepayment>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.PrincipalPart).HasPrecision(19, 4);
            e.Property(x => x.InterestPart).HasPrecision(19, 4);
            e.Property(x => x.IdempotencyKey).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(500);

            e.HasOne(x => x.Loan)
                .WithMany()
                .HasForeignKey(x => x.LoanId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.LoanId, x.IdempotencyKey }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "ck_loan_repayments_amounts",
                "amount > 0 AND principal_part >= 0 AND interest_part >= 0 AND principal_part + interest_part = amount"));
        });

        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);

            e.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_users_role",
                "role IN ('SuperAdmin','Manager','Teller','Auditor')"));
        });
    }
}
