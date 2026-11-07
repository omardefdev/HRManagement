using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Admin.Logistics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WF045_AssetApprovalLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AssetLossReports_AssetId",
                table: "AssetLossReports");

            migrationBuilder.AddColumn<int>(
                name: "ApprovalStartAttempts",
                table: "AssetLossReports",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalStartLastAttemptAt",
                table: "AssetLossReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStartLastError",
                table: "AssetLossReports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsIntervention",
                table: "AssetLossReports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByName",
                table: "AssetLossReports",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WithdrawnAt",
                table: "AssetLossReports",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WithdrawnBy",
                table: "AssetLossReports",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawnReason",
                table: "AssetLossReports",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "AssetLossReports",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<int>(
                name: "ApprovalStartAttempts",
                table: "AssetAssignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovalStartLastAttemptAt",
                table: "AssetAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStartLastError",
                table: "AssetAssignments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsIntervention",
                table: "AssetAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByName",
                table: "AssetAssignments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WithdrawnAt",
                table: "AssetAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WithdrawnBy",
                table: "AssetAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WithdrawnReason",
                table: "AssetAssignments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResponseStatus = table.Column<int>(type: "integer", nullable: false),
                    ResponseBody = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetLossReports_AssetId",
                table: "AssetLossReports",
                column: "AssetId",
                unique: true,
                filter: "\"Status\" IN (0, 4)");

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_Key",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AssetLossReports_AssetId",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "ApprovalStartAttempts",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "ApprovalStartLastAttemptAt",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "ApprovalStartLastError",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "NeedsIntervention",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "RequestedByName",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "WithdrawnAt",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "WithdrawnBy",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "WithdrawnReason",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "AssetLossReports");

            migrationBuilder.DropColumn(
                name: "ApprovalStartAttempts",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "ApprovalStartLastAttemptAt",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "ApprovalStartLastError",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "NeedsIntervention",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "RequestedByName",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "WithdrawnAt",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "WithdrawnBy",
                table: "AssetAssignments");

            migrationBuilder.DropColumn(
                name: "WithdrawnReason",
                table: "AssetAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_AssetLossReports_AssetId",
                table: "AssetLossReports",
                column: "AssetId",
                unique: true,
                filter: "\"Status\" = 0");
        }
    }
}
