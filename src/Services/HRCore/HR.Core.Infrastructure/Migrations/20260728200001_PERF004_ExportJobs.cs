using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HR.Core.Infrastructure.Data;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

[DbContext(typeof(HRCoreDbContext))]
[Migration("20260728200001_PERF004_ExportJobs")]
public partial class PERF004_ExportJobs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ExportJobs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
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
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ExportJobs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ExportJobs_RequestedByUserId",
            table: "ExportJobs",
            column: "RequestedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ExportJobs_TenantId_Status_CreatedAt",
            table: "ExportJobs",
            columns: new[] { "TenantId", "Status", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ExportJobs");
    }
}
