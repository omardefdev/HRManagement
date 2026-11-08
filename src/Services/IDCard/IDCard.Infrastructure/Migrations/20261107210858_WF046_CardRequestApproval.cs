using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IDCard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WF046_CardRequestApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "CardRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedBy",
                table: "CardRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "CardRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesCardId",
                table: "CardRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubmissionNo",
                table: "CardRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "CardRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedBy",
                table: "CardRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "CardRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "CardRequests",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "TenantId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "ReplacesCardId",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "SubmissionNo",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "CardRequests");
        }
    }
}
