using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM030_UnifiedIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var isPostgres = migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL";

            // HCM-030 (A6): the link columns become unique. Duplicates are not repaired here (that would rewrite
            // business data) — the migration stops and names the table instead. Checked on the local acceptance
            // database before adding the indexes: 0 duplicates (reports/plan30-session1-handoff.md).
            if (isPostgres)
            {
                migrationBuilder.Sql("""
                    DO $$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM "CitizenApplications" WHERE "PromotedRecruitmentApplicationId" IS NOT NULL
                                   GROUP BY "PromotedRecruitmentApplicationId" HAVING count(*) > 1) THEN
                            RAISE EXCEPTION 'HCM030: duplicate CitizenApplications.PromotedRecruitmentApplicationId — resolve before migrating';
                        END IF;
                        IF EXISTS (SELECT 1 FROM "InternalApplications" WHERE "PromotedRecruitmentApplicationId" IS NOT NULL
                                   GROUP BY "PromotedRecruitmentApplicationId" HAVING count(*) > 1) THEN
                            RAISE EXCEPTION 'HCM030: duplicate InternalApplications.PromotedRecruitmentApplicationId — resolve before migrating';
                        END IF;
                    END $$;
                    """);
            }

            migrationBuilder.DropIndex(
                name: "IX_InternalApplications_PromotedRecruitmentApplicationId",
                table: "InternalApplications");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_PromotedRecruitmentApplicationId",
                table: "CitizenApplications");

            migrationBuilder.AddColumn<string>(
                name: "IntakeChannel",
                table: "RecruitmentApplications",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Hand-edited from "": rows not matched below were entered by HR.
                defaultValue: "HrEntered");

            // HCM-030 (A6): channel of the existing rows, from the existing links.
            if (isPostgres)
            {
                migrationBuilder.Sql("""
                    UPDATE "RecruitmentApplications" ra
                    SET "IntakeChannel" = CASE
                        WHEN EXISTS (SELECT 1 FROM "CitizenApplications" c WHERE c."PromotedRecruitmentApplicationId" = ra."Id") THEN 'Public'
                        WHEN ra."SourceInternalApplicationId" IS NOT NULL
                          OR EXISTS (SELECT 1 FROM "InternalApplications" i WHERE i."PromotedRecruitmentApplicationId" = ra."Id") THEN 'Internal'
                        WHEN ra."IsLegacyUnlinked" THEN 'Legacy'
                        ELSE 'HrEntered'
                    END;
                    """);
            }

            migrationBuilder.CreateTable(
                name: "RecruitmentApplicationEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NotificationsPlannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentApplicationEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentApplicationEvents_RecruitmentApplications_Recrui~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InternalApplications_PromotedRecruitmentApplicationId",
                table: "InternalApplications",
                column: "PromotedRecruitmentApplicationId",
                unique: true,
                filter: "\"PromotedRecruitmentApplicationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_PromotedRecruitmentApplicationId",
                table: "CitizenApplications",
                column: "PromotedRecruitmentApplicationId",
                unique: true,
                filter: "\"PromotedRecruitmentApplicationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplicationEvents_NotificationsPending",
                table: "RecruitmentApplicationEvents",
                column: "OccurredAt",
                filter: "\"NotificationsPlannedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplicationEvents_RecruitmentApplicationId",
                table: "RecruitmentApplicationEvents",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplicationEvents_TenantId_RecruitmentApplicatio~",
                table: "RecruitmentApplicationEvents",
                columns: new[] { "TenantId", "RecruitmentApplicationId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitmentApplicationEvents");

            migrationBuilder.DropIndex(
                name: "IX_InternalApplications_PromotedRecruitmentApplicationId",
                table: "InternalApplications");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_PromotedRecruitmentApplicationId",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "IntakeChannel",
                table: "RecruitmentApplications");

            migrationBuilder.CreateIndex(
                name: "IX_InternalApplications_PromotedRecruitmentApplicationId",
                table: "InternalApplications",
                column: "PromotedRecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_PromotedRecruitmentApplicationId",
                table: "CitizenApplications",
                column: "PromotedRecruitmentApplicationId");
        }
    }
}
