using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dev.Analytics.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN006_AnalyticsOrgRebuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastFingerprint",
                table: "Reports",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScopeGrain",
                table: "Reports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SourceEvent",
                table: "Reports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastFingerprint",
                table: "ReadModels",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceEventName",
                table: "ReadModels",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "AnalyticsEvents",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                table: "AnalyticsEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExportType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FilterJson = table.Column<string>(type: "text", nullable: false),
                    AuthSnapshotJson = table.Column<string>(type: "text", nullable: false),
                    TemplateCode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TemplateVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LeaseUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ResultFileId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultBucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResultObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ResultContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResultFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_IdempotencyKey",
                table: "AnalyticsEvents",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_TenantId_EventName_OrgUnitId",
                table: "AnalyticsEvents",
                columns: new[] { "TenantId", "EventName", "OrgUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_RequestedByUserId",
                table: "ExportJobs",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_TenantId_Status_CreatedAt",
                table: "ExportJobs",
                columns: new[] { "TenantId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportJobs");

            migrationBuilder.DropIndex(
                name: "IX_AnalyticsEvents_IdempotencyKey",
                table: "AnalyticsEvents");

            migrationBuilder.DropIndex(
                name: "IX_AnalyticsEvents_TenantId_EventName_OrgUnitId",
                table: "AnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "LastFingerprint",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "ScopeGrain",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "SourceEvent",
                table: "Reports");

            migrationBuilder.DropColumn(
                name: "LastFingerprint",
                table: "ReadModels");

            migrationBuilder.DropColumn(
                name: "SourceEventName",
                table: "ReadModels");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "AnalyticsEvents");

            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                table: "AnalyticsEvents");
        }
    }
}
