using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PAY054_LegalFrameworkPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PLAN-54 (P-12). Every existing row takes its value from a COLUMN DEFAULT, never from an UPDATE: under forced row
            // security an UPDATE by the migrator role touches no row of a tenant table (PLAN-43), while a default given with
            // ADD COLUMN ... NOT NULL fills every row whoever runs it. So, with no data statement in this migration:
            //   - every existing framework version gets HashSchemaVersion = 1 and keeps its ContentHash untouched;
            //   - every existing allowance, deduction and adjustment gets ApprovalStatus = 'Approved' and Origin = 'System';
            //   - every existing bracket, holiday and priority rule gets LegalStatus = 'Interim' (no reference, no exception);
            //   - every existing attestation gets InterimExceptionIdsJson = '[]'.
            // The model declares none of these defaults: EF writes each value explicitly on every insert. They stay on the columns
            // for a row written by plain SQL.
            migrationBuilder.DropIndex(
                name: "IX_LegalDecisionRecords_DecisionId",
                table: "LegalDecisionRecords");

            migrationBuilder.AddColumn<string>(
                name: "InterimExceptionId",
                table: "TaxBrackets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalReference",
                table: "TaxBrackets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalStatus",
                table: "TaxBrackets",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Interim");

            migrationBuilder.AddColumn<string>(
                name: "Log",
                table: "PayrollRunJobs",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HashSchemaVersion",
                table: "PayrollConfigurations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "PayrollAdjustments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PayrollAdjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "PayrollAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "PayrollAdjustments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "PayrollAdjustments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<string>(
                name: "InterimExceptionId",
                table: "OfficialHolidays",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalReference",
                table: "OfficialHolidays",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalStatus",
                table: "OfficialHolidays",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Interim");

            migrationBuilder.AddColumn<string>(
                name: "InterimExceptionIdsJson",
                table: "LegalAttestations",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "InterimExceptionId",
                table: "DisbursementPriorityRules",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalStatus",
                table: "DisbursementPriorityRules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Interim");

            migrationBuilder.AddColumn<Guid>(
                name: "PayrollConfigurationId",
                table: "DisbursementPriorityRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "DeductionRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "DeductionRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "DeductionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "DeductionRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "DeductionRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "AllowanceRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "AllowanceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "AllowanceRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DecisionNote",
                table: "AllowanceRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "AllowanceRecords",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AllowanceRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayrollConfigurationEditors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollConfigurationEditors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollConfigurationEditors_PayrollConfigurations_PayrollCo~",
                        column: x => x.PayrollConfigurationId,
                        principalTable: "PayrollConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PayrollConfigurationFieldSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LegalReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LegalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InterimExceptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollConfigurationFieldSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollConfigurationFieldSources_PayrollConfigurations_Payr~",
                        column: x => x.PayrollConfigurationId,
                        principalTable: "PayrollConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatutoryDeductionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PayrollConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EmployeePercent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployerPercent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FixedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Ceiling = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    InsuranceBody = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    LegalReference = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LegalStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    InterimExceptionId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatutoryDeductionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatutoryDeductionRules_PayrollConfigurations_PayrollConfig~",
                        column: x => x.PayrollConfigurationId,
                        principalTable: "PayrollConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollAdjustments_Tenant_ApprovalStatus",
                table: "PayrollAdjustments",
                columns: new[] { "TenantId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDecisionRecords_Tenant_DecisionId",
                table: "LegalDecisionRecords",
                columns: new[] { "TenantId", "DecisionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisbursementPriorityRules_PayrollConfigurationId",
                table: "DisbursementPriorityRules",
                column: "PayrollConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_DeductionRecords_Tenant_ApprovalStatus",
                table: "DeductionRecords",
                columns: new[] { "TenantId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_AllowanceRecords_Tenant_ApprovalStatus",
                table: "AllowanceRecords",
                columns: new[] { "TenantId", "ApprovalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurationEditors_Configuration_User",
                table: "PayrollConfigurationEditors",
                columns: new[] { "PayrollConfigurationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PayrollConfigurationFieldSources_Configuration_Field",
                table: "PayrollConfigurationFieldSources",
                columns: new[] { "PayrollConfigurationId", "FieldName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatutoryDeductionRules_Configuration_Code",
                table: "StatutoryDeductionRules",
                columns: new[] { "PayrollConfigurationId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DisbursementPriorityRules_PayrollConfigurations_PayrollConf~",
                table: "DisbursementPriorityRules",
                column: "PayrollConfigurationId",
                principalTable: "PayrollConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // The three new tables are tenant tables: forced row security with the tenant policy (USING and WITH CHECK), as PAY053.
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
                TenantRlsMigrationHelper.EnableRls(
                    migrationBuilder,
                    "StatutoryDeductionRules",
                    "PayrollConfigurationFieldSources",
                    "PayrollConfigurationEditors");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DisbursementPriorityRules_PayrollConfigurations_PayrollConf~",
                table: "DisbursementPriorityRules");

            migrationBuilder.DropTable(
                name: "PayrollConfigurationEditors");

            migrationBuilder.DropTable(
                name: "PayrollConfigurationFieldSources");

            migrationBuilder.DropTable(
                name: "StatutoryDeductionRules");

            migrationBuilder.DropIndex(
                name: "IX_PayrollAdjustments_Tenant_ApprovalStatus",
                table: "PayrollAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_LegalDecisionRecords_Tenant_DecisionId",
                table: "LegalDecisionRecords");

            migrationBuilder.DropIndex(
                name: "IX_DisbursementPriorityRules_PayrollConfigurationId",
                table: "DisbursementPriorityRules");

            migrationBuilder.DropIndex(
                name: "IX_DeductionRecords_Tenant_ApprovalStatus",
                table: "DeductionRecords");

            migrationBuilder.DropIndex(
                name: "IX_AllowanceRecords_Tenant_ApprovalStatus",
                table: "AllowanceRecords");

            migrationBuilder.DropColumn(
                name: "InterimExceptionId",
                table: "TaxBrackets");

            migrationBuilder.DropColumn(
                name: "LegalReference",
                table: "TaxBrackets");

            migrationBuilder.DropColumn(
                name: "LegalStatus",
                table: "TaxBrackets");

            migrationBuilder.DropColumn(
                name: "Log",
                table: "PayrollRunJobs");

            migrationBuilder.DropColumn(
                name: "HashSchemaVersion",
                table: "PayrollConfigurations");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "PayrollAdjustments");

            migrationBuilder.DropColumn(
                name: "InterimExceptionId",
                table: "OfficialHolidays");

            migrationBuilder.DropColumn(
                name: "LegalReference",
                table: "OfficialHolidays");

            migrationBuilder.DropColumn(
                name: "LegalStatus",
                table: "OfficialHolidays");

            migrationBuilder.DropColumn(
                name: "InterimExceptionIdsJson",
                table: "LegalAttestations");

            migrationBuilder.DropColumn(
                name: "InterimExceptionId",
                table: "DisbursementPriorityRules");

            migrationBuilder.DropColumn(
                name: "LegalStatus",
                table: "DisbursementPriorityRules");

            migrationBuilder.DropColumn(
                name: "PayrollConfigurationId",
                table: "DisbursementPriorityRules");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "DeductionRecords");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "AllowanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "AllowanceRecords");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "AllowanceRecords");

            migrationBuilder.DropColumn(
                name: "DecisionNote",
                table: "AllowanceRecords");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "AllowanceRecords");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "AllowanceRecords");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDecisionRecords_DecisionId",
                table: "LegalDecisionRecords",
                column: "DecisionId",
                unique: true);
        }
    }
}
