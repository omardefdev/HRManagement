using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dev.Analytics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ANA032_RecruitmentRequisitionFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecruitmentRequisitionFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Applicants = table.Column<int>(type: "integer", nullable: false),
                    HiredCount = table.Column<int>(type: "integer", nullable: false),
                    TimeToFillDays = table.Column<int>(type: "integer", nullable: true),
                    ApplicantsPublic = table.Column<int>(type: "integer", nullable: false),
                    ApplicantsInternal = table.Column<int>(type: "integer", nullable: false),
                    ApplicantsHrEntered = table.Column<int>(type: "integer", nullable: false),
                    ApplicantsLegacy = table.Column<int>(type: "integer", nullable: false),
                    OffersAccepted = table.Column<int>(type: "integer", nullable: false),
                    OffersDeclined = table.Column<int>(type: "integer", nullable: false),
                    OffersExpired = table.Column<int>(type: "integer", nullable: false),
                    OffersRevoked = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentRequisitionFacts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitionFacts_TenantId_ClosedAt",
                table: "RecruitmentRequisitionFacts",
                columns: new[] { "TenantId", "ClosedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitionFacts_TenantId_RequisitionId",
                table: "RecruitmentRequisitionFacts",
                columns: new[] { "TenantId", "RequisitionId" },
                unique: true);

            // TEN-002C: the new tenant table gets the same RLS hardening as the others (idempotent for existing tables).
            HRManagement.ServiceDefaults.TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitmentRequisitionFacts");
        }
    }
}
