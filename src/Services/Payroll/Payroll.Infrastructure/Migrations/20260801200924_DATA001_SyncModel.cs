using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA001_SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LegalAttestations_PayrollConfigurations_PayrollConfiguratio~",
                table: "LegalAttestations");

            migrationBuilder.DropForeignKey(
                name: "FK_SalaryLineComponents_SalaryLines_SalaryLineId",
                table: "SalaryLineComponents");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PaymentBatches",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BankReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BeneficiaryIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    ValueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchedPaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayrollRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SalaryLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BeneficiaryIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SettlementReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SettledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_MatchedPaymentOrderId",
                table: "BankStatementLines",
                column: "MatchedPaymentOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_TenantId_StatementReference",
                table: "BankStatementLines",
                columns: new[] { "TenantId", "StatementReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PaymentBatchId",
                table: "PaymentOrders",
                column: "PaymentBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PayrollRunId_EmployeeId",
                table: "PaymentOrders",
                columns: new[] { "PayrollRunId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_TenantId_IdempotencyKey",
                table: "PaymentOrders",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LegalAttestations_PayrollConfigurations_PayrollConfiguratio~",
                table: "LegalAttestations",
                column: "PayrollConfigurationId",
                principalTable: "PayrollConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalaryLineComponents_SalaryLines_SalaryLineId",
                table: "SalaryLineComponents",
                column: "SalaryLineId",
                principalTable: "SalaryLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LegalAttestations_PayrollConfigurations_PayrollConfiguratio~",
                table: "LegalAttestations");

            migrationBuilder.DropForeignKey(
                name: "FK_SalaryLineComponents_SalaryLines_SalaryLineId",
                table: "SalaryLineComponents");

            migrationBuilder.DropTable(
                name: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "PaymentOrders");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PaymentBatches");

            migrationBuilder.AddForeignKey(
                name: "FK_LegalAttestations_PayrollConfigurations_PayrollConfiguratio~",
                table: "LegalAttestations",
                column: "PayrollConfigurationId",
                principalTable: "PayrollConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalaryLineComponents_SalaryLines_SalaryLineId",
                table: "SalaryLineComponents",
                column: "SalaryLineId",
                principalTable: "SalaryLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
