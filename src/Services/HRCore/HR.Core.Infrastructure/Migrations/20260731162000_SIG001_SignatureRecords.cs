using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using HR.Core.Infrastructure.Data;

#nullable disable

namespace HR.Core.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(HRCoreDbContext))]
[Migration("20260731162000_SIG001_SignatureRecords")]
public partial class SIG001_SignatureRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SignatureRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SignerSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                SignerRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Intent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CertificateThumbprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                CanonicalPayloadHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SignatureBase64 = table.Column<string>(type: "text", nullable: false),
                Algorithm = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LtvStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SignatureRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_SignatureRecords_TenantId_SubjectType_SubjectId",
            table: "SignatureRecords",
            columns: new[] { "TenantId", "SubjectType", "SubjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_SignatureRecords_TenantId_SignerSubject",
            table: "SignatureRecords",
            columns: new[] { "TenantId", "SignerSubject" });

        migrationBuilder.CreateIndex(
            name: "IX_SignatureRecords_CertificateThumbprint",
            table: "SignatureRecords",
            column: "CertificateThumbprint");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "SignatureRecords");
    }
}
