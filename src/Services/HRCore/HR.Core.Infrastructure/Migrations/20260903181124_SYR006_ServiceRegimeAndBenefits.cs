using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR006_ServiceRegimeAndBenefits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegalRegimeId",
                table: "EmploymentRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegalRegimes",
                columns: table => new
                {
                    LegalRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RegimeName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    LegislationRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResolvedLegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AllowedEmploymentTypes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GradeStructureId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    HasPromotionRules = table.Column<bool>(type: "boolean", nullable: false),
                    CoversPension = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransitionDecisionRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConflictNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalRegimes", x => x.LegalRegimeId);
                    table.ForeignKey(
                        name: "FK_LegalRegimes_LegislationSources_ResolvedLegalSourceId",
                        column: x => x.ResolvedLegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "BenefitPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PlanName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PlanType = table.Column<int>(type: "integer", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EmployeeContributionRate = table.Column<decimal>(type: "numeric", nullable: false),
                    EmployerContributionRate = table.Column<decimal>(type: "numeric", nullable: false),
                    CeilingAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    EligibilityMinServiceMonths = table.Column<int>(type: "integer", nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LegalRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BenefitPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BenefitPlans_LegalRegimes_LegalRegimeId",
                        column: x => x.LegalRegimeId,
                        principalTable: "LegalRegimes",
                        principalColumn: "LegalRegimeId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BenefitPlans_LegislationSources_LegalSourceId",
                        column: x => x.LegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmploymentRecords_LegalRegimeId",
                table: "EmploymentRecords",
                column: "LegalRegimeId");

            migrationBuilder.CreateIndex(
                name: "IX_BenefitPlans_LegalRegimeId",
                table: "BenefitPlans",
                column: "LegalRegimeId");

            migrationBuilder.CreateIndex(
                name: "IX_BenefitPlans_LegalSourceId",
                table: "BenefitPlans",
                column: "LegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_BenefitPlans_TenantId_PlanCode",
                table: "BenefitPlans",
                columns: new[] { "TenantId", "PlanCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalRegimes_IsActive",
                table: "LegalRegimes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_LegalRegimes_ResolvedLegalSourceId",
                table: "LegalRegimes",
                column: "ResolvedLegalSourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_EmploymentRecords_LegalRegimes_LegalRegimeId",
                table: "EmploymentRecords",
                column: "LegalRegimeId",
                principalTable: "LegalRegimes",
                principalColumn: "LegalRegimeId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EmploymentRecords_LegalRegimes_LegalRegimeId",
                table: "EmploymentRecords");

            migrationBuilder.DropTable(
                name: "BenefitPlans");

            migrationBuilder.DropTable(
                name: "LegalRegimes");

            migrationBuilder.DropIndex(
                name: "IX_EmploymentRecords_LegalRegimeId",
                table: "EmploymentRecords");

            migrationBuilder.DropColumn(
                name: "LegalRegimeId",
                table: "EmploymentRecords");
        }
    }
}
