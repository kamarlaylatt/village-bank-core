using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VillageBank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    member_id = table.Column<long>(type: "bigint", nullable: false),
                    principal = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    monthly_interest_rate_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    term_months = table.Column<int>(type: "integer", nullable: false),
                    purpose = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    applied_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    applied_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    disbursed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    disbursed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    disbursement_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loans", x => x.id);
                    table.CheckConstraint("ck_loans_status", "status IN ('Pending','Approved','Rejected','Active','Closed')");
                    table.CheckConstraint("ck_loans_terms", "principal > 0 AND term_months BETWEEN 1 AND 60 AND monthly_interest_rate_percent >= 0");
                    table.ForeignKey(
                        name: "fk_loans_journal_entries_disbursement_journal_entry_id",
                        column: x => x.disbursement_journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loans_members_member_id",
                        column: x => x.member_id,
                        principalTable: "members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loans_users_applied_by_user_id",
                        column: x => x.applied_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loans_users_decided_by_user_id",
                        column: x => x.decided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loans_users_disbursed_by_user_id",
                        column: x => x.disbursed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loan_installments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    principal_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    interest_due = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    principal_paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    interest_paid = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_installments", x => x.id);
                    table.CheckConstraint("ck_loan_installments_amounts", "principal_due >= 0 AND interest_due >= 0 AND principal_paid >= 0 AND interest_paid >= 0 AND principal_paid <= principal_due AND interest_paid <= interest_due");
                    table.ForeignKey(
                        name: "fk_loan_installments_loans_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "loan_repayments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    principal_part = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    interest_part = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_loan_repayments", x => x.id);
                    table.CheckConstraint("ck_loan_repayments_amounts", "amount > 0 AND principal_part >= 0 AND interest_part >= 0 AND principal_part + interest_part = amount");
                    table.ForeignKey(
                        name: "fk_loan_repayments_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_repayments_loans_loan_id",
                        column: x => x.loan_id,
                        principalTable: "loans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_loan_repayments_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loan_installments_loan_id_number",
                table: "loan_installments",
                columns: new[] { "loan_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loan_repayments_created_by_user_id",
                table: "loan_repayments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_repayments_journal_entry_id",
                table: "loan_repayments",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_loan_repayments_loan_id_idempotency_key",
                table: "loan_repayments",
                columns: new[] { "loan_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loans_applied_by_user_id",
                table: "loans",
                column: "applied_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_decided_by_user_id",
                table: "loans",
                column: "decided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_disbursed_by_user_id",
                table: "loans",
                column: "disbursed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_disbursement_journal_entry_id",
                table: "loans",
                column: "disbursement_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_loans_member_id",
                table: "loans",
                column: "member_id",
                unique: true,
                filter: "status NOT IN ('Rejected','Closed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loan_installments");

            migrationBuilder.DropTable(
                name: "loan_repayments");

            migrationBuilder.DropTable(
                name: "loans");
        }
    }
}
