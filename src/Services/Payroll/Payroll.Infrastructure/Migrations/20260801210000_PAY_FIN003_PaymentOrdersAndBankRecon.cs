using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY_FIN003_PaymentOrdersAndBankRecon : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_PaymentOrders", x => x.Id));

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BankReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BeneficiaryIban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    ValueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchedPaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_BankStatementLines", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_TenantId_IdempotencyKey",
                table: "PaymentOrders",
                columns: new[] { "TenantId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_TenantId_StatementReference",
                table: "BankStatementLines",
                columns: new[] { "TenantId", "StatementReference" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "BankStatementLines");
            migrationBuilder.DropTable(name: "PaymentOrders");
        }
    }
}
