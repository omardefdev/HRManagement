using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM028_AppealGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RankBeforeRejection",
                table: "RecruitmentApplications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionKind",
                table: "RecruitmentApplications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionNote",
                table: "RecruitmentApplications",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalInfo",
                table: "RecruitmentAppeals",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdditionalInfoAt",
                table: "RecruitmentAppeals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AssignedAt",
                table: "RecruitmentAppeals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedBy",
                table: "RecruitmentAppeals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrectedRank",
                table: "RecruitmentAppeals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiledBy",
                table: "RecruitmentAppeals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FiledByEmployeeId",
                table: "RecruitmentAppeals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndependentReviewerEmployeeId",
                table: "RecruitmentAppeals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InfoRequest",
                table: "RecruitmentAppeals",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InfoRequestedAt",
                table: "RecruitmentAppeals",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "RecruitmentAppeals",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // HCM-028 (H4): the direct-reopen path (ReopenAppeal) is removed. Its history — AppealNotes on applications
            // without any RecruitmentAppeal row — becomes a decided appeal with Outcome LegacyReopen. Insert-only:
            // applications are not modified. Status 2 = RecruitmentAppealStatus.Decided (stored as integer).
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    INSERT INTO "RecruitmentAppeals"
                        ("Id", "TenantId", "RecruitmentApplicationId", "Status", "Outcome", "Reason", "Decision",
                         "DecidedAt", "FiledBy", "CreatedAt", "UpdatedAt")
                    SELECT gen_random_uuid(), a."TenantId", a."Id", 2, 'LegacyReopen', left(btrim(a."AppealNotes"), 2000),
                           'مسار قديم — إعادة فتح مباشرة', a."ReviewedAt", 'HrOnBehalf',
                           COALESCE(a."ReviewedAt", now()), COALESCE(a."ReviewedAt", now())
                    FROM "RecruitmentApplications" a
                    WHERE a."AppealNotes" IS NOT NULL
                      AND btrim(a."AppealNotes") <> ''
                      AND NOT EXISTS (SELECT 1 FROM "RecruitmentAppeals" r WHERE r."RecruitmentApplicationId" = a."Id");
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The LegacyReopen appeal rows inserted by Up are kept (history); re-running Up does not duplicate them.
            migrationBuilder.DropColumn(
                name: "RankBeforeRejection",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "RejectionKind",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "RejectionNote",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "AdditionalInfo",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "AdditionalInfoAt",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "AssignedAt",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "AssignedBy",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "CorrectedRank",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "FiledBy",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "FiledByEmployeeId",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "IndependentReviewerEmployeeId",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "InfoRequest",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "InfoRequestedAt",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "RecruitmentAppeals");
        }
    }
}
