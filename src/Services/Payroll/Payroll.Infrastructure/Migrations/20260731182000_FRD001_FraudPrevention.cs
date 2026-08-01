using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payroll.Infrastructure.Data;

#nullable disable

namespace Payroll.Infrastructure.Migrations;

/// <summary>FRD-001: fraud alerts, payment holds, and proof-of-life records.</summary>
[DbContext(typeof(PayrollDbContext))]
[Migration("20260731182000_FRD001_FraudPrevention")]
public partial class FRD001_FraudPrevention : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FraudAlerts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PayrollRunId = table.Column<Guid>(type: "uuid", nullable: true),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                RuleId = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ResolutionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ResolvedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PaymentHoldId = table.Column<Guid>(type: "uuid", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_FraudAlerts", x => x.Id));

        migrationBuilder.CreateTable(
            name: "PaymentHolds",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                PayrollRunId = table.Column<Guid>(type: "uuid", nullable: true),
                FraudAlertId = table.Column<Guid>(type: "uuid", nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                HoldType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ReleasedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ReleaseApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                ReleaseReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                EvidenceRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                HeldAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_PaymentHolds", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ProofOfLifeRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                VerificationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ExceptionReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                VerifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ProofOfLifeRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_FraudAlerts_Status",
            table: "FraudAlerts",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_FraudAlerts_EmployeeId",
            table: "FraudAlerts",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_FraudAlerts_PayrollRunId",
            table: "FraudAlerts",
            column: "PayrollRunId");

        migrationBuilder.CreateIndex(
            name: "IX_FraudAlerts_Tenant_Status_Detected",
            table: "FraudAlerts",
            columns: new[] { "TenantId", "Status", "DetectedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_PaymentHolds_Status",
            table: "PaymentHolds",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentHolds_EmployeeId",
            table: "PaymentHolds",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentHolds_FraudAlertId",
            table: "PaymentHolds",
            column: "FraudAlertId");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentHolds_Tenant_Status_Held",
            table: "PaymentHolds",
            columns: new[] { "TenantId", "Status", "HeldAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ProofOfLifeRecords_EmployeeId",
            table: "ProofOfLifeRecords",
            column: "EmployeeId");

        migrationBuilder.CreateIndex(
            name: "IX_ProofOfLifeRecords_Status",
            table: "ProofOfLifeRecords",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_ProofOfLifeRecords_ExpiryDate",
            table: "ProofOfLifeRecords",
            column: "ExpiryDate");

        TenantRlsMigrationHelper.EnableRls(
            migrationBuilder,
            "FraudAlerts",
            "PaymentHolds",
            "ProofOfLifeRecords");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FraudAlerts");
        migrationBuilder.DropTable(name: "PaymentHolds");
        migrationBuilder.DropTable(name: "ProofOfLifeRecords");
    }
}
