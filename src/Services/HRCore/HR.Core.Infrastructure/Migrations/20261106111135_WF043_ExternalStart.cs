using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WF043_ExternalStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsUrl",
                table: "WorkflowInstances",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayTitle",
                table: "WorkflowInstances",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExcludedApproverUserIdsJson",
                table: "WorkflowInstances",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "WorkflowInstances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByName",
                table: "WorkflowInstances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "WorkflowInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ScopeOrgUnitId",
                table: "WorkflowInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceClientId",
                table: "WorkflowInstances",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectEmployeeId",
                table: "WorkflowInstances",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailsUrl",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "DisplayTitle",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ExcludedApproverUserIdsJson",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "RequestedByName",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "ScopeOrgUnitId",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "SourceClientId",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "SubjectEmployeeId",
                table: "WorkflowInstances");
        }
    }
}
