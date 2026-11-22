using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-53 (P-12): the salary change requests and the reference change requests (two tenant tables with row security, a filtered
    /// unique index for the one open request of an employee and of a reference record), and two relations turned to Restrict: a grade a
    /// pay profile names and a scale that has grades are no longer deleted (they were SetNull and Cascade). No data changes.
    /// </summary>
    public partial class PAY053_SalaryChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePayProfiles_PayGrades_PayGradeId",
                table: "EmployeePayProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_PayGrades_SalaryScales_SalaryScaleId",
                table: "PayGrades");

            migrationBuilder.CreateTable(
                name: "PayProfileChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangeKind = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CurrentProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentBaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CurrentPayGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProposedBaseSalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ProposedPayGradeId = table.Column<Guid>(type: "uuid", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LegalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppliedProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastApplyError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SupersededByRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayProfileChangeRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollReferenceChangeRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<int>(type: "integer", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    EntityLabel = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    Operation = table.Column<int>(type: "integer", nullable: false),
                    ProposedJson = table.Column<string>(type: "text", nullable: true),
                    CurrentJson = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LegalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelledByName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollReferenceChangeRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayProfileChangeRequests_OneOpen",
                table: "PayProfileChangeRequests",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "\"State\" IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_PayProfileChangeRequests_OrgUnitId",
                table: "PayProfileChangeRequests",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PayProfileChangeRequests_State_EffectiveFrom",
                table: "PayProfileChangeRequests",
                columns: new[] { "State", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReferenceChangeRequests_OneOpen",
                table: "PayrollReferenceChangeRequests",
                columns: new[] { "TenantId", "EntityType", "EntityId" },
                unique: true,
                filter: "\"State\" = 0 AND \"EntityId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReferenceChangeRequests_State",
                table: "PayrollReferenceChangeRequests",
                column: "State");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePayProfiles_PayGrades_PayGradeId",
                table: "EmployeePayProfiles",
                column: "PayGradeId",
                principalTable: "PayGrades",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayGrades_SalaryScales_SalaryScaleId",
                table: "PayGrades",
                column: "SalaryScaleId",
                principalTable: "SalaryScales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // PLAN-53 (P-12): both request tables are tenant tables: forced row security with the tenant policy (USING and WITH CHECK).
            // The migration changes no data, so no FORCE has to be lifted (PLAN-43's lesson does not apply).
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                TenantRlsMigrationHelper.EnableRls(migrationBuilder, "PayProfileChangeRequests", "PayrollReferenceChangeRequests");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmployeePayProfiles_PayGrades_PayGradeId",
                table: "EmployeePayProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_PayGrades_SalaryScales_SalaryScaleId",
                table: "PayGrades");

            migrationBuilder.DropTable(
                name: "PayProfileChangeRequests");

            migrationBuilder.DropTable(
                name: "PayrollReferenceChangeRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_EmployeePayProfiles_PayGrades_PayGradeId",
                table: "EmployeePayProfiles",
                column: "PayGradeId",
                principalTable: "PayGrades",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PayGrades_SalaryScales_SalaryScaleId",
                table: "PayGrades",
                column: "SalaryScaleId",
                principalTable: "SalaryScales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
