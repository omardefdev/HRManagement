using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY002_ImmutableConfigVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SalaryLines_PayrollRunId",
                table: "SalaryLines");

            migrationBuilder.DropIndex(
                name: "IX_PayrollConfigurations_Code",
                table: "PayrollConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentBatches_PayrollRunId",
                table: "PaymentBatches");

            migrationBuilder.DropIndex(
                name: "IX_EmployeePayProfiles_EmployeeId_IsActive",
                table: "EmployeePayProfiles");

            migrationBuilder.AddColumn<string>(
                name: "ConfigContentHash",
                table: "PayrollRuns",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollConfigurationId",
                table: "PayrollRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "PayrollRuns",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PayrollConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "PayrollConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "PayrollConfigurations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "PayrollConfigurations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DecimalPlaces",
                table: "PayrollConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveFrom",
                table: "PayrollConfigurations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "EffectiveTo",
                table: "PayrollConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicationStatus",
                table: "PayrollConfigurations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "PayrollConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedBy",
                table: "PayrollConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundingMode",
                table: "PayrollConfigurations",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesConfigurationId",
                table: "PayrollConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "PayrollConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // PAY-002: backfill existing configs as published v1 with defaults
            migrationBuilder.Sql("""
                UPDATE "PayrollConfigurations"
                SET "VersionNumber" = CASE WHEN "VersionNumber" = 0 THEN 1 ELSE "VersionNumber" END,
                    "PublicationStatus" = CASE WHEN "PublicationStatus" = '' OR "PublicationStatus" IS NULL THEN 'Published' ELSE "PublicationStatus" END,
                    "CurrencyCode" = CASE WHEN "CurrencyCode" = '' OR "CurrencyCode" IS NULL THEN 'SYP' ELSE "CurrencyCode" END,
                    "DecimalPlaces" = CASE WHEN "DecimalPlaces" = 0 THEN 2 ELSE "DecimalPlaces" END,
                    "RoundingMode" = CASE WHEN "RoundingMode" = '' OR "RoundingMode" IS NULL THEN 'AwayFromZero' ELSE "RoundingMode" END,
                    "EffectiveFrom" = CASE WHEN "EffectiveFrom" = '-infinity'::timestamp OR "EffectiveFrom" < TIMESTAMPTZ '2000-01-01' THEN TIMESTAMPTZ '2000-01-01 00:00:00+00' ELSE "EffectiveFrom" END,
                    "PublishedAt" = COALESCE("PublishedAt", NOW() AT TIME ZONE 'utc'),
                    "ApprovedAt" = COALESCE("ApprovedAt", NOW() AT TIME ZONE 'utc');
                """);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "EmployeePayProfiles",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLines_Run_Employee",
                table: "SalaryLines",
                columns: new[] { "PayrollRunId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns",
                columns: new[] { "TenantId", "PeriodStart", "PeriodEnd", "RunType" },
                unique: true,
                filter: "\"Status\" <> 5");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_PayrollConfigurationId",
                table: "PayrollRuns",
                column: "PayrollConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurations_SupersedesConfigurationId",
                table: "PayrollConfigurations",
                column: "SupersedesConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurations_Tenant_Active",
                table: "PayrollConfigurations",
                columns: new[] { "TenantId", "IsActive" },
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurations_Tenant_Code_Version",
                table: "PayrollConfigurations",
                columns: new[] { "TenantId", "Code", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatches_Run_BankRef",
                table: "PaymentBatches",
                columns: new[] { "PayrollRunId", "BankTransferReference" },
                unique: true,
                filter: "\"BankTransferReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayProfiles_OneActive",
                table: "EmployeePayProfiles",
                column: "EmployeeId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_Key",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollConfigurations_PayrollConfigurations_SupersedesConfi~",
                table: "PayrollConfigurations",
                column: "SupersedesConfigurationId",
                principalTable: "PayrollConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollRuns_PayrollConfigurations_PayrollConfigurationId",
                table: "PayrollRuns",
                column: "PayrollConfigurationId",
                principalTable: "PayrollConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PayrollConfigurations_PayrollConfigurations_SupersedesConfi~",
                table: "PayrollConfigurations");

            migrationBuilder.DropForeignKey(
                name: "FK_PayrollRuns_PayrollConfigurations_PayrollConfigurationId",
                table: "PayrollRuns");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_SalaryLines_Run_Employee",
                table: "SalaryLines");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_LivePeriod",
                table: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_PayrollRuns_PayrollConfigurationId",
                table: "PayrollRuns");

            migrationBuilder.DropIndex(
                name: "IX_PayrollConfigurations_SupersedesConfigurationId",
                table: "PayrollConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_PayrollConfigurations_Tenant_Active",
                table: "PayrollConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_PayrollConfigurations_Tenant_Code_Version",
                table: "PayrollConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentBatches_Run_BankRef",
                table: "PaymentBatches");

            migrationBuilder.DropIndex(
                name: "IX_EmployeePayProfiles_OneActive",
                table: "EmployeePayProfiles");

            migrationBuilder.DropColumn(
                name: "ConfigContentHash",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "PayrollConfigurationId",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "PayrollRuns");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "DecimalPlaces",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "EffectiveFrom",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "EffectiveTo",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "PublicationStatus",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "PublishedBy",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "RoundingMode",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "SupersedesConfigurationId",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "EmployeePayProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryLines_PayrollRunId",
                table: "SalaryLines",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurations_Code",
                table: "PayrollConfigurations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentBatches_PayrollRunId",
                table: "PaymentBatches",
                column: "PayrollRunId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeePayProfiles_EmployeeId_IsActive",
                table: "EmployeePayProfiles",
                columns: new[] { "EmployeeId", "IsActive" });
        }
    }
}
