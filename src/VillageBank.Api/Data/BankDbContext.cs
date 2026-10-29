using Microsoft.EntityFrameworkCore;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Data;

public class BankDbContext : DbContext
{
    public BankDbContext(DbContextOptions<BankDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

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
    }
}
