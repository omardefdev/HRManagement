using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compliance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WF046_DisciplinaryApproval : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// PLAN-46: the actions published before this plan get their publication time. An UPDATE by the migrator matches no row under
        /// forced row security (PLAN-43), so the force flag is lifted for this statement only, inside the migration's transaction, and
        /// put back exactly as it was (PLAN-44's pattern).
        /// </summary>
        private const string StampPublishedBeforeEngine = """
            DO $$
            DECLARE
                forced boolean;
            BEGIN
                SELECT relforcerowsecurity INTO forced FROM pg_class WHERE oid = '"DisciplinaryActions"'::regclass;
                IF forced THEN
                    EXECUTE 'ALTER TABLE "DisciplinaryActions" NO FORCE ROW LEVEL SECURITY';
                END IF;

                UPDATE "DisciplinaryActions"
                SET "OutcomePublishedAt" = "CreatedAt"
                WHERE "ApprovedBeforeEngine" AND "OutcomePublishedAt" IS NULL;

                IF forced THEN
                    EXECUTE 'ALTER TABLE "DisciplinaryActions" FORCE ROW LEVEL SECURITY';
                END IF;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // PLAN-46 (P-3): every action that exists now was published to payroll when it was created. A column default fills the
            // existing rows whatever row security says: they become Approved (2) and ApprovedBeforeEngine, and are never published again.
            // The defaults stay: this code always writes both columns, while the previous tag, if it is rolled back to, omits them and
            // publishes at once, so its rows are rightly approved-before-engine as well.
            migrationBuilder.AddColumn<int>(
                name: "ApprovalStatus",
                table: "DisciplinaryActions",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<bool>(
                name: "ApprovedBeforeEngine",
                table: "DisciplinaryActions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "DisciplinaryActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedBy",
                table: "DisciplinaryActions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutcomePublishedAt",
                table: "DisciplinaryActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "DisciplinaryActions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmissionNo",
                table: "DisciplinaryActions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "DisciplinaryActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedBy",
                table: "DisciplinaryActions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "DisciplinaryActions",
                type: "uuid",
                nullable: true);

            // xmin is a system column: Npgsql emits nothing for it, and the previous tag runs beside this schema unchanged.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "DisciplinaryActions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "TenantId" });

            migrationBuilder.Sql(StampPublishedBeforeEngine);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "ApprovedBeforeEngine",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "OutcomePublishedAt",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "SubmissionNo",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "DisciplinaryActions");
        }
    }
}
