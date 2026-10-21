using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM026_CriteriaGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedBy",
                table: "RecruitmentAssessmentCriteria",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                table: "RecruitmentAssessmentCriteria",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "RecruitmentAssessmentCriteria",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "RecruitmentAssessmentCriteria",
                type: "timestamp with time zone",
                nullable: true);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // HCM-026: the latest version per requisition becomes the active one.
                migrationBuilder.Sql("""
                    UPDATE "RecruitmentAssessmentCriteria" AS c
                    SET "IsActive" = true
                    WHERE c."VersionNumber" = (
                        SELECT MAX(c2."VersionNumber")
                        FROM "RecruitmentAssessmentCriteria" AS c2
                        WHERE c2."TenantId" IS NOT DISTINCT FROM c."TenantId"
                          AND c2."RequisitionId" = c."RequisitionId");
                    """);

                // HCM-026: versions already used by legacy free-form scores are treated as locked.
                migrationBuilder.Sql("""
                    UPDATE "RecruitmentAssessmentCriteria" AS c
                    SET "LockedAt" = now() AT TIME ZONE 'utc'
                    WHERE c."LockedAt" IS NULL
                      AND EXISTS (
                        SELECT 1 FROM "RecruitmentApplications" AS a
                        WHERE a."CriteriaVersionId" = c."Id"
                          AND a."AssessmentScore" IS NOT NULL);
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAssessmentCriteria_Tenant_Requisition_Active",
                table: "RecruitmentAssessmentCriteria",
                columns: new[] { "TenantId", "RequisitionId" },
                unique: true,
                filter: "\"IsActive\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecruitmentAssessmentCriteria_Tenant_Requisition_Active",
                table: "RecruitmentAssessmentCriteria");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                table: "RecruitmentAssessmentCriteria");

            migrationBuilder.DropColumn(
                name: "ChangeReason",
                table: "RecruitmentAssessmentCriteria");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "RecruitmentAssessmentCriteria");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "RecruitmentAssessmentCriteria");
        }
    }
}
