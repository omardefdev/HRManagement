using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using IT.Infra.Infrastructure.Data;
using HRManagement.ServiceDefaults;

#nullable disable

namespace IT.Infra.Infrastructure.Migrations;

[DbContext(typeof(ITInfraDbContext))]
[Migration("20260731184000_DEC001_Decommission")]
public partial class DEC001_Decommission : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LegacySystems",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                SystemName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                CutoffDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Phase = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ArchiveManifestHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ArchiveUri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_LegacySystems", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_LegacySystems_TenantId_SystemName", table: "LegacySystems", columns: new[] { "TenantId", "SystemName" });
        migrationBuilder.CreateIndex(name: "IX_LegacySystems_Phase", table: "LegacySystems", column: "Phase");

        migrationBuilder.CreateTable(
            name: "ShadowOperationChecks",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                LegacySystemId = table.Column<Guid>(type: "uuid", nullable: false),
                AuditDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                FindingsCount = table.Column<int>(type: "integer", nullable: false),
                Exceptions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Auditor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ShadowOperationChecks", x => x.Id));

        migrationBuilder.CreateTable(
            name: "MediaDestructionCertificates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                LegacySystemId = table.Column<Guid>(type: "uuid", nullable: false),
                Custodian = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                DestroyedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CertificateHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_MediaDestructionCertificates", x => x.Id));

        TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "MediaDestructionCertificates");
        migrationBuilder.DropTable(name: "ShadowOperationChecks");
        migrationBuilder.DropTable(name: "LegacySystems");
    }
}
