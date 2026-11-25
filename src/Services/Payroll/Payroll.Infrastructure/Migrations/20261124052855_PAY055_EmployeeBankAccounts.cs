using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-55 (G-PAY-02): the employee's bank account as its own entity, registered by one person and verified by another, and the
    /// account a run pins on its snapshots and lines.
    /// <para>
    /// <b>The data move.</b> An account number an older build left on a pay profile becomes one account <c>PendingVerification</c> per
    /// employee and agency (the number of the active profile, else of the latest profile that carries one): never an implicit
    /// verification, and with no registrar, so it can only be rejected and registered again by a person. The pay profiles are under forced
    /// row security and the migrator carries no agency, so without lifting <c>FORCE</c> the <c>INSERT … SELECT</c> would read no row and
    /// report success (PLAN-43's lesson; <c>WF044</c>'s way). The owner is exempt once FORCE is off; ALTER TABLE holds an exclusive lock
    /// until the commit and rolls back with the migration, so no other session ever sees the table unforced. The new table gets its row
    /// security after the move: under it, the migrator could not insert a row of any agency.
    /// </para>
    /// <para>
    /// The three bank columns of <c>EmployeePayProfiles</c> stay: the previous tag reads them after a rollback. Nothing reads them now.
    /// </para>
    /// </summary>
    public partial class PAY055_EmployeeBankAccounts : Migration
    {
        /// <summary>The pay profiles' table, read by the data move under a lifted FORCE.</summary>
        internal const string ProfilesTable = "EmployeePayProfiles";

        /// <summary>
        /// One pending account (Status 0, kind IBAN 0) per employee and agency whose pay profiles carry a number. The number is normalised
        /// as the validator does (no white space, upper case); the verification date of the old field is deliberately not carried.
        /// </summary>
        internal const string MoveProfileAccountsSql = """
            INSERT INTO "EmployeeBankAccounts"
                ("Id", "EmployeeId", "AccountKind", "AccountNumber", "BankNameAr", "Status", "TenantId", "CreatedAt", "UpdatedAt")
            SELECT gen_random_uuid(), moved."EmployeeId", 0, moved.number, moved.bank, 0, moved."TenantId", now(), now()
            FROM (
                SELECT DISTINCT ON (p."TenantId", p."EmployeeId")
                       p."TenantId",
                       p."EmployeeId",
                       upper(regexp_replace(p."BankAccountIban", '\s', '', 'g')) AS number,
                       NULLIF(btrim(p."BankName"), '') AS bank
                FROM "EmployeePayProfiles" p
                WHERE p."BankAccountIban" IS NOT NULL AND btrim(p."BankAccountIban") <> ''
                ORDER BY p."TenantId", p."EmployeeId", p."IsActive" DESC, p."EffectiveFrom" DESC, p."Id"
            ) moved;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "SalaryLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BankAccountId",
                table: "PayrollRunInputSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmployeeBankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountKind = table.Column<int>(type: "integer", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: false),
                    BankCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BankNameAr = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    HolderNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    VerifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerificationReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RejectedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RetiredBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetirementReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupersedesAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeBankAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLines_BankAccountId",
                table: "SalaryLines",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunInputSnapshots_BankAccountId",
                table: "PayrollRunInputSnapshots",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBankAccounts_EmployeeId",
                table: "EmployeeBankAccounts",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeBankAccounts_OneActiveOnePending",
                table: "EmployeeBankAccounts",
                columns: new[] { "TenantId", "EmployeeId", "Status" },
                unique: true,
                filter: "\"Status\" IN (0, 1)");

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRunInputSnapshots_EmployeeBankAccounts_BankAccountId",
                table: "PayrollRunInputSnapshots",
                column: "BankAccountId",
                principalTable: "EmployeeBankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalaryLines_EmployeeBankAccounts_BankAccountId",
                table: "SalaryLines",
                column: "BankAccountId",
                principalTable: "EmployeeBankAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // The data move, as the owner with FORCE lifted on the table it reads (see the summary).
                migrationBuilder.Sql($"""ALTER TABLE "{ProfilesTable}" NO FORCE ROW LEVEL SECURITY;""");
                migrationBuilder.Sql(MoveProfileAccountsSql);
                migrationBuilder.Sql($"""ALTER TABLE "{ProfilesTable}" FORCE ROW LEVEL SECURITY;""");

                // The new table is a tenant table: forced row security with the tenant policy (USING and WITH CHECK), as PAY054.
                TenantRlsMigrationHelper.EnableRls(migrationBuilder, "EmployeeBankAccounts");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRunInputSnapshots_EmployeeBankAccounts_BankAccountId",
                table: "PayrollRunInputSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_SalaryLines_EmployeeBankAccounts_BankAccountId",
                table: "SalaryLines");

            migrationBuilder.DropTable(
                name: "EmployeeBankAccounts");

            migrationBuilder.DropIndex(
                name: "IX_SalaryLines_BankAccountId",
                table: "SalaryLines");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRunInputSnapshots_BankAccountId",
                table: "PayrollRunInputSnapshots");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "SalaryLines");

            migrationBuilder.DropColumn(
                name: "BankAccountId",
                table: "PayrollRunInputSnapshots");
        }
    }
}
