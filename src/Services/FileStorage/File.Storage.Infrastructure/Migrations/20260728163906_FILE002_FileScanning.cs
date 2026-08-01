using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace File.Storage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FILE002_FileScanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CdrApplied",
                table: "Files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CdrProfile",
                table: "Files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectedContentType",
                table: "Files",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanStatus",
                table: "Files",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PendingScan");

            migrationBuilder.AddColumn<DateTime>(
                name: "ScannedAt",
                table: "Files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScannerName",
                table: "Files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScannerVersion",
                table: "Files",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThreatName",
                table: "Files",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FileScanResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ThreatName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ScannerName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ScannerVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CdrApplied = table.Column<bool>(type: "boolean", nullable: false),
                    CdrProfile = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileScanResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileScanWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileScanWorkItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Files_ScanStatus",
                table: "Files",
                column: "ScanStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FileScanResults_FileId",
                table: "FileScanResults",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FileScanResults_ScannedAt",
                table: "FileScanResults",
                column: "ScannedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FileScanWorkItems_FileId",
                table: "FileScanWorkItems",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FileScanWorkItems_Status_NextAttemptAt",
                table: "FileScanWorkItems",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileScanResults");

            migrationBuilder.DropTable(
                name: "FileScanWorkItems");

            migrationBuilder.DropIndex(
                name: "IX_Files_ScanStatus",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "CdrApplied",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "CdrProfile",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "DetectedContentType",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "ScanStatus",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "ScannedAt",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "ScannerName",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "ScannerVersion",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "ThreatName",
                table: "Files");
        }
    }
}
