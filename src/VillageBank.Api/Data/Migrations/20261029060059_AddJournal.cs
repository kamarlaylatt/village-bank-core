using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VillageBank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    source_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "journal_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    debit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    credit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_journal_lines", x => x.id);
                    table.CheckConstraint("ck_journal_lines_one_sided", "debit >= 0 AND credit >= 0 AND ((debit > 0 AND credit = 0) OR (credit > 0 AND debit = 0))");
                    table.ForeignKey(
                        name: "fk_journal_lines_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_journal_lines_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_source_type_source_id",
                table: "journal_entries",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_account_id",
                table: "journal_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_lines_journal_entry_id",
                table: "journal_lines",
                column: "journal_entry_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "journal_lines");

            migrationBuilder.DropTable(
                name: "journal_entries");
        }
    }
}
