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

            migrationBuilder.AddColumn<string>(
                name: "EditorUserIdsJson",
                table: "WorkflowDefinitions",
                type: "text",
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

            // PLAN-43: no data backfill here. WorkflowDefinitions has forced row security and the migrator role carries no tenant, so
            // an UPDATE would match no row. WorkflowPackSeeder.BackfillGovernanceAsync does it at start-up, inside the tenant.
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
                name: "EditorUserIdsJson",
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
