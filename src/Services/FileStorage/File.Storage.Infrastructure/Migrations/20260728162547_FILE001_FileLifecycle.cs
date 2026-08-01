using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace File.Storage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FILE001_FileLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ForceDeleteReason",
                table: "Files",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LegalHold",
                table: "Files",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ObjectPurgedAt",
                table: "Files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetainUntil",
                table: "Files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FilePurgeWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
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
                    table.PrimaryKey("PK_FilePurgeWorkItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileReconcileExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Bucket = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileReconcileExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileReferenceOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    HolderKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileReferenceOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilePurgeWorkItems_FileId",
                table: "FilePurgeWorkItems",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FilePurgeWorkItems_Status_NextAttemptAt",
                table: "FilePurgeWorkItems",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FileReconcileExceptions_FileId",
                table: "FileReconcileExceptions",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_FileReconcileExceptions_Kind_Approved",
                table: "FileReconcileExceptions",
                columns: new[] { "Kind", "Approved" });

            migrationBuilder.CreateIndex(
                name: "IX_FileReferenceOutbox_FileId_HolderKey_Operation_Status",
                table: "FileReferenceOutbox",
                columns: new[] { "FileId", "HolderKey", "Operation", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_FileReferenceOutbox_Status_NextAttemptAt",
                table: "FileReferenceOutbox",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilePurgeWorkItems");

            migrationBuilder.DropTable(
                name: "FileReconcileExceptions");

            migrationBuilder.DropTable(
                name: "FileReferenceOutbox");

            migrationBuilder.DropColumn(
                name: "ForceDeleteReason",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "LegalHold",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "ObjectPurgedAt",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "RetainUntil",
                table: "Files");
        }
    }
}
