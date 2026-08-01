using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payroll.Infrastructure.Data;

#nullable disable

namespace Payroll.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(PayrollDbContext))]
[Migration("20260731183000_PAY004_ArrearsAndPartialDisbursement")]
public partial class PAY004_ArrearsAndPartialDisbursement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "DisbursedAmount",
            table: "PaymentBatches",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<Guid>(
            name: "FundingDecisionId",
            table: "PaymentBatches",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsPartial",
            table: "PaymentBatches",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "ArrearsLedgerEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                ArrearsAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CurrencyEra = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SettledByBatchId = table.Column<Guid>(type: "uuid", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ArrearsLedgerEntries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "DisbursementPriorityRules",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Priority = table.Column<int>(type: "integer", nullable: false),
                MinimumAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                LegalSource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DisbursementPriorityRules", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FundingDecisions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                ApprovedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                RequestedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                FundingRatio = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PriorityRuleSetId = table.Column<Guid>(type: "uuid", nullable: true),
                DecisionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ApprovedBy = table.Column<Guid>(type: "uuid", nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FundingDecisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_FundingDecisions_PayrollRuns_PayrollRunId",
                    column: x => x.PayrollRunId,
                    principalTable: "PayrollRuns",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentBatches_FundingDecisionId",
            table: "PaymentBatches",
            column: "FundingDecisionId");

        migrationBuilder.CreateIndex(
            name: "IX_ArrearsLedgerEntries_EmployeeId",
            table: "ArrearsLedgerEntries",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_ArrearsLedgerEntries_Employee_Status",
            table: "ArrearsLedgerEntries",
            columns: new[] { "EmployeeId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_ArrearsLedgerEntries_PayrollRunId",
            table: "ArrearsLedgerEntries",
            column: "PayrollRunId");

        migrationBuilder.CreateIndex(
            name: "IX_ArrearsLedgerEntries_SettledByBatchId",
            table: "ArrearsLedgerEntries",
            column: "SettledByBatchId");

        migrationBuilder.CreateIndex(
            name: "IX_DisbursementPriorityRules_RuleSet_Category",
            table: "DisbursementPriorityRules",
            columns: new[] { "RuleSetId", "Category" });

        migrationBuilder.CreateIndex(
            name: "IX_DisbursementPriorityRules_RuleSet_Priority",
            table: "DisbursementPriorityRules",
            columns: new[] { "RuleSetId", "Priority" });

        migrationBuilder.CreateIndex(
            name: "IX_FundingDecisions_PayrollRunId",
            table: "FundingDecisions",
            column: "PayrollRunId");

        migrationBuilder.CreateIndex(
            name: "IX_FundingDecisions_PriorityRuleSetId",
            table: "FundingDecisions",
            column: "PriorityRuleSetId");

        migrationBuilder.AddForeignKey(
            name: "FK_PaymentBatches_FundingDecisions_FundingDecisionId",
            table: "PaymentBatches",
            column: "FundingDecisionId",
            principalTable: "FundingDecisions",
            principalColumn: "Id",
            onDelete: ReferentialAction.SetNull);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_PaymentBatches_FundingDecisions_FundingDecisionId",
            table: "PaymentBatches");

        migrationBuilder.DropTable(
            name: "ArrearsLedgerEntries");

        migrationBuilder.DropTable(
            name: "DisbursementPriorityRules");

        migrationBuilder.DropTable(
            name: "FundingDecisions");

        migrationBuilder.DropIndex(
            name: "IX_PaymentBatches_FundingDecisionId",
            table: "PaymentBatches");

        migrationBuilder.DropColumn(
            name: "DisbursedAmount",
            table: "PaymentBatches");

        migrationBuilder.DropColumn(
            name: "FundingDecisionId",
            table: "PaymentBatches");

        migrationBuilder.DropColumn(
            name: "IsPartial",
            table: "PaymentBatches");
    }
}
