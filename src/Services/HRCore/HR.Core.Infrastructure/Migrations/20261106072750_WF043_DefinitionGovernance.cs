using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WF043_DefinitionGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeReason",
                table: "WorkflowDefinitions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastEditedBy",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "WorkflowDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublishedBy",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireDistinctApprovers",
                table: "WorkflowDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAt",
                table: "WorkflowDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetiredBy",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "WorkflowDefinitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "WorkflowDefinitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedBy",
                table: "WorkflowDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "WorkflowDefinitions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // PLAN-43: every published row becomes Published; an unpublished one is Retired when its code is retired
            // (WorkflowPackSeeder.RetiredCodes), otherwise a Draft. Only custody requires distinct approvers.
            migrationBuilder.Sql("""
                UPDATE "WorkflowDefinitions"
                SET "Status" = CASE
                        WHEN "IsPublished" THEN 'Published'
                        WHEN "Code" IN ('RECRUITMENT-HIRE') THEN 'Retired'
                        ELSE 'Draft'
                    END,
                    "RequireDistinctApprovers" = ("Code" = 'ASSET-ASSIGN-APPROVAL');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeReason",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "LastEditedBy",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "PublishedBy",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "RequireDistinctApprovers",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "RetiredBy",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "WorkflowDefinitions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "WorkflowDefinitions");
        }
    }
}
