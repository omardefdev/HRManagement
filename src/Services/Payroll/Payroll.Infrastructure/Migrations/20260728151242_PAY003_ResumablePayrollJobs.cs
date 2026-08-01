using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY003_ResumablePayrollJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns");

            migrationBuilder.AddColumn<bool>(
                name: "ResultsFrozen",
                table: "PayrollRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ValidatedAtCalculate",
                table: "PayrollRuns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BatchKind",
                table: "PaymentBatches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Disbursement");

            migrationBuilder.AddColumn<Guid>(
                name: "ReversesBatchId",
                table: "PaymentBatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "PaymentBatches",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Processed");

            migrationBuilder.CreateTable(
                name: "PayrollRunJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCheckpointAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PartitionSize = table.Column<int>(type: "integer", nullable: false),
                    ResultsValidated = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunJobs_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunInputSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeePayProfileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProfileVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfigContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunInputSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunInputSnapshots_PayrollRunJobs_PayrollRunJobId",
                        column: x => x.PayrollRunJobId,
                        principalTable: "PayrollRunJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunPartitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartitionIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    EmployeeCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedCount = table.Column<int>(type: "integer", nullable: false),
                    CursorEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunPartitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunPartitions_PayrollRunJobs_PayrollRunJobId",
                        column: x => x.PayrollRunJobId,
                        principalTable: "PayrollRunJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "RunType" },
                unique: true,
                filter: "\"Status\" <> 5 AND \"Status\" <> 8");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunInputSnapshots_PayrollRunJobId_EmployeeId",
                table: "PayrollRunInputSnapshots",
                columns: new[] { "PayrollRunJobId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunInputSnapshots_PayrollRunJobId_SortOrder",
                table: "PayrollRunInputSnapshots",
                columns: new[] { "PayrollRunJobId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunJobs_ActivePerRun",
                table: "PayrollRunJobs",
                column: "PayrollRunId",
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Running', 'Paused')");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunPartitions_PayrollRunJobId_PartitionIndex",
                table: "PayrollRunPartitions",
                columns: new[] { "PayrollRunJobId", "PartitionIndex" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PayrollRunInputSnapshots");

            migrationBuilder.DropTable(
                name: "PayrollRunPartitions");

            migrationBuilder.DropTable(
                name: "PayrollRunJobs");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ResultsFrozen",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ValidatedAtCalculate",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "BatchKind",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "ReversesBatchId",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PaymentBatches");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "RunType" },
                unique: true,
                filter: "\"Status\" <> 5");
        }
    }
}
