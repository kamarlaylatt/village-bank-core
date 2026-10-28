using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace VillageBank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.id);
                    table.CheckConstraint("ck_accounts_type", "type IN ('Asset','Liability','Equity','Income','Expense')");
                });

            migrationBuilder.InsertData(
                table: "accounts",
                columns: new[] { "id", "code", "is_active", "name", "type" },
                values: new object[,]
                {
                    { 1, "1000", true, "Cash", "Asset" },
                    { 2, "1200", true, "Loans Receivable", "Asset" },
                    { 3, "2000", true, "Member Savings", "Liability" },
                    { 4, "3000", true, "Share Capital", "Equity" },
                    { 5, "4000", true, "Interest Income", "Income" },
                    { 6, "4100", true, "Penalty Income", "Income" },
                    { 7, "5000", true, "Interest Expense on Savings", "Expense" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_accounts_code",
                table: "accounts",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
