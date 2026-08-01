using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using File.Storage.Infrastructure.Data;
using HRManagement.ServiceDefaults;

#nullable disable

namespace File.Storage.Infrastructure.Migrations;

/// <summary>SEC-003: FileAccessGrants table + audit decision columns + RLS.</summary>
[DbContext(typeof(FileStorageDbContext))]
[Migration("20260731180000_SEC003_FileAccessGrants")]
public partial class SEC003_FileAccessGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FileAccessGrants",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                FileId = table.Column<Guid>(type: "uuid", nullable: false),
                Module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                HolderType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                HolderId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FileReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Visibility = table.Column<int>(type: "integer", nullable: false),
                GranteeId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                PresignedUrlMaxLifetimeSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 300),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_FileAccessGrants", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_FileAccessGrants_TenantId_FileId",
            table: "FileAccessGrants",
            columns: new[] { "TenantId", "FileId" });

        migrationBuilder.CreateIndex(
            name: "IX_FileAccessGrants_TenantId_FileReference",
            table: "FileAccessGrants",
            columns: new[] { "TenantId", "FileReference" });

        migrationBuilder.CreateIndex(
            name: "IX_FileAccessGrants_TenantId_HolderType_HolderId",
            table: "FileAccessGrants",
            columns: new[] { "TenantId", "HolderType", "HolderId" });

        migrationBuilder.CreateIndex(
            name: "IX_FileAccessGrants_FileId_IsRevoked",
            table: "FileAccessGrants",
            columns: new[] { "FileId", "IsRevoked" });

        migrationBuilder.AddColumn<bool>(
            name: "DecisionAllowed",
            table: "FileAccessAuditLogs",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "DecisionReason",
            table: "FileAccessAuditLogs",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        // Backfill Module grants for existing active files (uploader as grantee when known).
        migrationBuilder.Sql("""
            INSERT INTO "FileAccessGrants" (
                "Id", "TenantId", "FileId", "Module", "HolderType", "HolderId",
                "FileReference", "Visibility", "GranteeId", "PresignedUrlMaxLifetimeSeconds",
                "IsRevoked", "CreatedAtUtc", "RevokedAtUtc")
            SELECT
                gen_random_uuid(),
                f."TenantId",
                f."Id",
                'File.Storage',
                'Uploader',
                COALESCE(f."UploadedBy"::text, 'system'),
                f."Bucket" || '/' || f."ObjectKey",
                1,
                f."UploadedBy"::text,
                300,
                false,
                NOW() AT TIME ZONE 'utc',
                NULL
            FROM "Files" f
            WHERE f."IsDeleted" = false
              AND NOT EXISTS (
                  SELECT 1 FROM "FileAccessGrants" g WHERE g."FileId" = f."Id");
            """);

        TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FileAccessGrants");
        migrationBuilder.DropColumn(name: "DecisionAllowed", table: "FileAccessAuditLogs");
        migrationBuilder.DropColumn(name: "DecisionReason", table: "FileAccessAuditLogs");
    }
}
