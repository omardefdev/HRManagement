using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM026_RequisitionIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ApplicationClosingDate",
                table: "RecruitmentRequisitions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "RecruitmentRequisitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "RecruitmentRequisitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledBy",
                table: "RecruitmentRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IntakeClosedAt",
                table: "RecruitmentRequisitions",
                type: "timestamp with time zone",
                nullable: true);

            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // HCM-026: refuse to build the index over existing duplicates — they must be resolved by hand.
                migrationBuilder.Sql("""
                    DO $$
                    BEGIN
                        IF EXISTS (
                            SELECT 1
                            FROM "RecruitmentRequisitions"
                            WHERE "Status" IN ('Draft','PendingApproval','Approved','Published')
                            GROUP BY "TenantId", "FundedPositionId"
                            HAVING COUNT(*) > 1)
                        THEN
                            RAISE EXCEPTION 'HCM026: duplicate active requisitions per funded position — run scripts/hcm026-find-duplicate-requisitions.ps1';
                        END IF;
                    END
                    $$;
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitions_Tenant_Position_Blocking",
                table: "RecruitmentRequisitions",
                columns: new[] { "TenantId", "FundedPositionId" },
                unique: true,
                filter: "\"Status\" IN ('Draft','PendingApproval','Approved','Published')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RecruitmentRequisitions_Tenant_Position_Blocking",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "ApplicationClosingDate",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "IntakeClosedAt",
                table: "RecruitmentRequisitions");
        }
    }
}
