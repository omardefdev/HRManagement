using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-57 (§5): the attendance handover in payroll's schema.
    /// <list type="bullet">
    /// <item><c>AttendanceSummaryReceipts</c>: one row per attendance summary (unique <c>SummaryId</c>, <c>xmin</c>), a tenant table
    /// under forced row security with the tenant policy (the hand-written block at the end of <c>Up</c>).</item>
    /// <item><c>AbsenceDeductions</c> gains <c>OriginalPeriodStart</c>, <c>OriginalPeriodEnd</c>, <c>CarryForward</c> (false for
    /// every row that exists) and <c>SourceVersion</c>, and the index (<c>EmployeeId</c>, <c>CarryForward</c>, <c>AppliedInRunId</c>).</item>
    /// </list>
    /// No row is moved or changed: the rows of the old close (<c>AttendancePeriodClosed:…</c>) and of unpaid leave keep their
    /// behaviour, with no original period. The previous tag ignores the new table and columns.
    /// </summary>
    public partial class LVA057_AttendanceHandoff : Migration
    {
        /// <summary>The tenant table this migration creates: it gets forced row security with the tenant policy.</summary>
        public static readonly string[] NewTenantTables = ["AttendanceSummaryReceipts"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CarryForward",
                table: "AbsenceDeductions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalPeriodEnd",
                table: "AbsenceDeductions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OriginalPeriodStart",
                table: "AbsenceDeductions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceVersion",
                table: "AbsenceDeductions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceSummaryReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    LatestVersion = table.Column<int>(type: "integer", nullable: false),
                    LatestAbsentDays = table.Column<int>(type: "integer", nullable: false),
                    RecordedAbsentDays = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSummaryReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDeductions_EmployeeId_CarryForward_AppliedInRunId",
                table: "AbsenceDeductions",
                columns: new[] { "EmployeeId", "CarryForward", "AppliedInRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSummaryReceipts_EmployeeId_PeriodStart_PeriodEnd",
                table: "AttendanceSummaryReceipts",
                columns: new[] { "EmployeeId", "PeriodStart", "PeriodEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSummaryReceipts_SummaryId",
                table: "AttendanceSummaryReceipts",
                column: "SummaryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSummaryReceipts_TenantId_PeriodStart",
                table: "AttendanceSummaryReceipts",
                columns: new[] { "TenantId", "PeriodStart" });

            // --- hand-written (PLAN-57): the new tenant table under forced row security (USING and WITH CHECK), as PAY056 ---
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                foreach (var table in NewTenantTables)
                    TenantRlsMigrationHelper.EnableRls(migrationBuilder, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceSummaryReceipts");

            migrationBuilder.DropIndex(
                name: "IX_AbsenceDeductions_EmployeeId_CarryForward_AppliedInRunId",
                table: "AbsenceDeductions");

            migrationBuilder.DropColumn(
                name: "CarryForward",
                table: "AbsenceDeductions");

            migrationBuilder.DropColumn(
                name: "OriginalPeriodEnd",
                table: "AbsenceDeductions");

            migrationBuilder.DropColumn(
                name: "OriginalPeriodStart",
                table: "AbsenceDeductions");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "AbsenceDeductions");
        }
    }
}
