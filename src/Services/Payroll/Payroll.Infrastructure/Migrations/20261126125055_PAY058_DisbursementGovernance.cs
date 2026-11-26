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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
