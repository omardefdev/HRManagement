using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY_FIN001_DeductionSchedulingAndPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AckedAt",
                table: "PaymentBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "PaymentBatches",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SettledAt",
                table: "PaymentBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettlementReference",
                table: "PaymentBatches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "PaymentBatches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "DeductionRecords",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstallmentNumber",
                table: "DeductionRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "DeductionRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "AbsenceDeductions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbsenceDeductions_SourceReference",
                table: "AbsenceDeductions",
                column: "SourceReference",
                unique: true,
                filter: "\"SourceReference\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AbsenceDeductions_SourceReference",
                table: "AbsenceDeductions");

            migrationBuilder.DropColumn(
                name: "AckedAt",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "SettledAt",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "SettlementReference",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "PaymentBatches");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "InstallmentNumber",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "AbsenceDeductions");
        }
    }
}
