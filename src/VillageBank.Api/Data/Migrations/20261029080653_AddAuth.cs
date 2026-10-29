using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VillageBank.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "created_by_user_id",
                table: "savings_transactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "created_by_user_id",
                table: "journal_entries",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('SuperAdmin','Manager','Teller','Auditor')");
                    table.ForeignKey(
                        name: "fk_users_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_savings_transactions_created_by_user_id",
                table: "savings_transactions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_journal_entries_created_by_user_id",
                table: "journal_entries",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_by_user_id",
                table: "users",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "users",
                column: "username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_journal_entries_users_created_by_user_id",
                table: "journal_entries",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_savings_transactions_users_created_by_user_id",
                table: "savings_transactions",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_journal_entries_users_created_by_user_id",
                table: "journal_entries");

            migrationBuilder.DropForeignKey(
                name: "fk_savings_transactions_users_created_by_user_id",
                table: "savings_transactions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropIndex(
                name: "ix_savings_transactions_created_by_user_id",
                table: "savings_transactions");

            migrationBuilder.DropIndex(
                name: "ix_journal_entries_created_by_user_id",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "savings_transactions");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "journal_entries");
        }
    }
}
