using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using File.Storage.Infrastructure.Data;

#nullable disable

namespace File.Storage.Infrastructure.Migrations;

/// <summary>SEC-003 AC-29: FileAccessGrant.ExpiresAtUtc for grant expiry enforcement.</summary>
[DbContext(typeof(FileStorageDbContext))]
[Migration("20260824190000_SEC003_GrantExpiresAt")]
public partial class SEC003_GrantExpiresAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ExpiresAtUtc",
            table: "FileAccessGrants",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "FileAccessGrants"
            SET "ExpiresAtUtc" = "CreatedAtUtc" + INTERVAL '24 hours'
            WHERE "ExpiresAtUtc" IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ExpiresAtUtc", table: "FileAccessGrants");
    }
}
