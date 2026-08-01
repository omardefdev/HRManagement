using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(GatewayAuditDbContext))]
[Migration("20260731190000_EVD003_EvidenceRepository")]
public partial class EVD003_EvidenceRepository : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EvidenceRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CausationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                Resource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Purpose = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                AssuranceLevel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Policy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Rule = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Result = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                BeforeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                AfterHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                EvidenceLinksJson = table.Column<string>(type: "text", nullable: true),
                ReleaseVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ConfigVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TimeSource = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ChainKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Sequence = table.Column<long>(type: "bigint", nullable: false),
                PrevHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                EventHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EvidenceRecords", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceRecords_ChainKey_Sequence",
            table: "EvidenceRecords",
            columns: new[] { "ChainKey", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceRecords_TenantId_CorrelationId",
            table: "EvidenceRecords",
            columns: new[] { "TenantId", "CorrelationId" });

        migrationBuilder.CreateTable(
            name: "EvidenceArtifacts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ArtifactId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                CardId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ControlId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ReleaseDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Signature = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                TrustedTimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Classification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StorageUri = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EvidenceArtifacts", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceArtifacts_ArtifactId",
            table: "EvidenceArtifacts",
            column: "ArtifactId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceArtifacts_CardId",
            table: "EvidenceArtifacts",
            column: "CardId");

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceArtifacts_ContentHash",
            table: "EvidenceArtifacts",
            column: "ContentHash");

        migrationBuilder.CreateIndex(
            name: "IX_EvidenceArtifacts_ExpiresAtUtc",
            table: "EvidenceArtifacts",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateTable(
            name: "EvidencePackExports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PackId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExportedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ExportedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ArtifactIdsJson = table.Column<string>(type: "text", nullable: false),
                PackHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Destination = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EvidencePackExports", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_EvidencePackExports_ExportedAtUtc",
            table: "EvidencePackExports",
            column: "ExportedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_EvidencePackExports_PackId",
            table: "EvidencePackExports",
            column: "PackId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "EvidencePackExports");
        migrationBuilder.DropTable(name: "EvidenceArtifacts");
        migrationBuilder.DropTable(name: "EvidenceRecords");
    }
}
