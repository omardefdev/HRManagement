using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-56 (G-PAY-04, G-PAY-06): the life of a payroll run in the schema.
    /// <list type="bullet">
    /// <item>The run: its version (<c>Revision</c>, 1 for every run that exists), who posted it and when, who cancelled it, when and why.</item>
    /// <item><c>PayrollRunRevisions</c>: what each rejected version of a run was. <c>PayrollReversalRequests</c>: a reversal is a request
    /// another person decides, one waiting per run.</item>
    /// <item><c>PayrollAdjustments.TargetSourceRunId</c>: an adjustment aimed at the correction of one posted run.</item>
    /// <item><c>IX_PayrollRuns_LivePeriod</c> is limited to regular runs (its status filter kept), and one correction not yet posted is
    /// allowed per source run. A final settlement is left with no uniqueness of its own: its key is PLAN-64's.</item>
    /// </list>
    /// No existing row changes but for the new columns' defaults. The two new tables are tenant tables: forced row security with the
    /// tenant policy, as <c>PAY055</c> (the hand-written block at the end of <c>Up</c>; everything else is generated).
    /// </summary>
    public partial class PAY056_RunLifecycle : Migration
    {
        /// <summary>The tenant tables this migration creates: each gets forced row security with the tenant policy.</summary>
        internal static readonly string[] NewTenantTables = ["PayrollRunRevisions", "PayrollReversalRequests"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "PayrollRuns",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "PayrollRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledBy",
                table: "PayrollRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAt",
                table: "PayrollRuns",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PostedBy",
                table: "PayrollRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "PayrollRuns",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetSourceRunId",
                table: "PayrollAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollReversalRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollReversalRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollReversalRequests_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRunRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    LineCount = table.Column<int>(type: "integer", nullable: false),
                    TotalNetPay = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LinesFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RejectedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRunRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollRunRevisions_PayrollRuns_PayrollRunId",
                        column: x => x.PayrollRunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "RunType" },
                unique: true,
                filter: "\"RunType\" = 'Regular' AND \"Status\" <> 5 AND \"Status\" <> 8");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_OneOpenCorrectionPerSource",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "SourceRunId" },
                unique: true,
                filter: "\"RunType\" = 'Correction' AND \"SourceRunId\" IS NOT NULL AND \"Status\" NOT IN (2, 5, 8)");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_TargetSourceRunId",
                table: "PayrollAdjustments",
                column: "TargetSourceRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReversalRequests_OnePending",
                table: "PayrollReversalRequests",
                columns: new[] { "TenantId", "PayrollRunId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReversalRequests_PayrollRunId",
                table: "PayrollReversalRequests",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollReversalRequests_TenantId_Status",
                table: "PayrollReversalRequests",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRunRevisions_Run_Revision",
                table: "PayrollRunRevisions",
                columns: new[] { "PayrollRunId", "Revision" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollAdjustments_PayrollRuns_TargetSourceRunId",
                table: "PayrollAdjustments",
                column: "TargetSourceRunId",
                principalTable: "PayrollRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // --- hand-written (PLAN-56): the new tenant tables under forced row security (USING and WITH CHECK), as PAY055 ---
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                foreach (var table in NewTenantTables)
                    TenantRlsMigrationHelper.EnableRls(migrationBuilder, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollAdjustments_PayrollRuns_TargetSourceRunId",
                table: "PayrollAdjustments");

            migrationBuilder.DropTable(
                name: "PayrollReversalRequests");

            migrationBuilder.DropTable(
                name: "PayrollRunRevisions");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_OneOpenCorrectionPerSource",
                table: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_TargetSourceRunId",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PostedBy",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "TargetSourceRunId",
                table: "PayrollAdjustments");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "RunType" },
                unique: true,
                filter: "\"Status\" <> 5 AND \"Status\" <> 8");
        }
    }
}
