using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VillageBank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_entry_date",
                table: "journal_entries",
                column: "entry_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_journal_entries_entry_date",
                table: "journal_entries");
        }
    }
}
