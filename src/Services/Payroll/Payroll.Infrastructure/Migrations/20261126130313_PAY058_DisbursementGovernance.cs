using HRManagement.ServiceDefaults;
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY058_DisbursementGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "PayrollRunApprovals",
                type: "integer",
                nullable: false,
                // PLAN-58: every decision older than this plan was taken on its run's first (and only counted) version.
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "HoldReason",
                table: "PaymentOrders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentBatchFileId",
                table: "PaymentOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ArrearsAmount",
                table: "PaymentBatches",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "PaymentBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PaymentBatchFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ControlCount = table.Column<int>(type: "integer", nullable: false),
                    ControlTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    GeneratedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupersededById = table.Column<Guid>(type: "uuid", nullable: true),
                    ReissueReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReissueApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentBatchFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentBatchFiles_PaymentBatches_PaymentBatchId",
                        column: x => x.PaymentBatchId,
                        principalTable: "PaymentBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PaymentBatchFileId",
                table: "PaymentOrders",
                column: "PaymentBatchFileId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatchFiles_Batch_Sequence",
                table: "PaymentBatchFiles",
                columns: new[] { "PaymentBatchId", "Sequence" },
                unique: true);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // PLAN-58: the new tables are tenant tables: forced row security with the tenant policy (USING and WITH CHECK), as PAY055.
                // No data moves in this migration.
                TenantRlsMigrationHelper.EnableRls(migrationBuilder, "PaymentBatchFiles");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentBatchFiles");

            migrationBuilder.DropIndex(
                name: "IX_PaymentOrders_PaymentBatchFileId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "PayrollRunApprovals");

            migrationBuilder.DropColumn(
                name: "HoldReason",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "PaymentBatchFileId",
                table: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "ArrearsAmount",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PaymentBatches");
        }
    }
}
