using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-57 (§1–§3): the attendance period in the schema.
    /// <list type="bullet">
    /// <item><c>AttendancePeriods</c>, <c>AttendancePeriodClosures</c>, <c>AttendanceEmployeeSummaries</c> (one row per period and
    /// employee, with <c>xmin</c>) and <c>AttendanceCorrections</c> (one pending request per employee and day): tenant tables under
    /// forced row security with the tenant policy (the hand-written block at the end of <c>Up</c>).</item>
    /// <item><c>DailyAttendanceRecords</c> gains <c>Source</c> (0, manual, for every record that exists), <c>SourceReference</c> and
    /// <c>ReplacedStatus</c>.</item>
    /// <item><b>The fill:</b> a record an approved leave made (<c>Status</c> = «في إجازة» and the note the leave consumer wrote,
    /// «إجازة معتمدة:…») gets <c>Source</c> = 2 (leave). Every other record stays manual, and nothing is deleted.</item>
    /// </list>
    /// <c>DailyAttendanceRecords</c> is under forced row security and the migrator carries no tenant, so without the first step the
    /// fill would see no row and report success (PLAN-43's lesson). The owner is exempt once FORCE is off, so the migration lifts it
    /// around the fill, in its own transaction, and puts it back, as <c>WF044</c> does. A role that does not own the table fails
    /// there, and nothing changes.
    /// </summary>
    public partial class LVA057_AttendancePeriods : Migration
    {
        /// <summary>The tenant tables this migration creates: each gets forced row security with the tenant policy.</summary>
        public static readonly string[] NewTenantTables =
            ["AttendancePeriods", "AttendancePeriodClosures", "AttendanceEmployeeSummaries", "AttendanceCorrections"];

        /// <summary>The fill: leave-made records are marked; AttendanceRecordSource.Leave = 2, DailyAttendanceStatus.OnLeave = 2.</summary>
        public const string MarkLeaveMadeRecords =
            """
            UPDATE "DailyAttendanceRecords"
            SET "Source" = 2
            WHERE "Status" = 2 AND "Notes" LIKE 'إجازة معتمدة:%';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReplacedStatus",
                table: "DailyAttendanceRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "DailyAttendanceRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "DailyAttendanceRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendancePeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OldStatus = table.Column<int>(type: "integer", nullable: true),
                    NewStatus = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SourceReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResultingVersion = table.Column<int>(type: "integer", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_AttendancePeriods_AttendancePeriodId",
                        column: x => x.AttendancePeriodId,
                        principalTable: "AttendancePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceCorrections_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendanceEmployeeSummaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    WorkingDays = table.Column<int>(type: "integer", nullable: false),
                    PresentDays = table.Column<int>(type: "integer", nullable: false),
                    AbsentDays = table.Column<int>(type: "integer", nullable: false),
                    OnLeaveDays = table.Column<int>(type: "integer", nullable: false),
                    RemoteDays = table.Column<int>(type: "integer", nullable: false),
                    UnrecordedWorkingDays = table.Column<int>(type: "integer", nullable: false),
                    LastReason = table.Column<int>(type: "integer", nullable: false),
                    LastCorrectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceEmployeeSummaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendanceEmployeeSummaries_AttendancePeriods_AttendancePer~",
                        column: x => x.AttendancePeriodId,
                        principalTable: "AttendancePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AttendanceEmployeeSummaries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AttendancePeriodClosures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttendancePeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EmployeeCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedAlreadyClosed = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendancePeriodClosures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttendancePeriodClosures_AttendancePeriods_AttendancePeriod~",
                        column: x => x.AttendancePeriodId,
                        principalTable: "AttendancePeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_AttendancePeriodId",
                table: "AttendanceCorrections",
                column: "AttendancePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_OnePendingPerDay",
                table: "AttendanceCorrections",
                columns: new[] { "EmployeeId", "AttendanceDate" },
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_OnePerLeaveEventDay",
                table: "AttendanceCorrections",
                columns: new[] { "EmployeeId", "AttendanceDate", "SourceReference" },
                unique: true,
                filter: "\"SourceReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceCorrections_TenantId_Status",
                table: "AttendanceCorrections",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEmployeeSummaries_AttendancePeriodId_EmployeeId",
                table: "AttendanceEmployeeSummaries",
                columns: new[] { "AttendancePeriodId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceEmployeeSummaries_EmployeeId",
                table: "AttendanceEmployeeSummaries",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriodClosures_AttendancePeriodId",
                table: "AttendancePeriodClosures",
                column: "AttendancePeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendancePeriods_TenantId_PeriodStart",
                table: "AttendancePeriods",
                columns: new[] { "TenantId", "PeriodStart" },
                unique: true);

            // --- hand-written (PLAN-57) ---
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // The fill reads and writes a tenant table: FORCE is lifted for the owner around it (see the summary).
                migrationBuilder.Sql("""ALTER TABLE "DailyAttendanceRecords" NO FORCE ROW LEVEL SECURITY;""");
                migrationBuilder.Sql(MarkLeaveMadeRecords);
                migrationBuilder.Sql("""ALTER TABLE "DailyAttendanceRecords" FORCE ROW LEVEL SECURITY;""");

                foreach (var table in NewTenantTables)
                    TenantRlsMigrationHelper.EnableRls(migrationBuilder, table);
            }
            else
            {
                migrationBuilder.Sql(MarkLeaveMadeRecords);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceCorrections");

            migrationBuilder.DropTable(
                name: "AttendanceEmployeeSummaries");

            migrationBuilder.DropTable(
                name: "AttendancePeriodClosures");

            migrationBuilder.DropTable(
                name: "AttendancePeriods");

            migrationBuilder.DropColumn(
                name: "ReplacedStatus",
                table: "DailyAttendanceRecords");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "DailyAttendanceRecords");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "DailyAttendanceRecords");
        }
    }
}
