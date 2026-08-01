using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Records.Archive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN006_ArchiveGov : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RetentionCode",
                table: "DocumentClassifications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovalEvidenceFileId",
                table: "DisposalRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletenessAttestation",
                table: "ArchiveItems",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisposeApprovedAt",
                table: "ArchiveItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisposeApprovedBy",
                table: "ArchiveItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisposeCancelReason",
                table: "ArchiveItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisposeRequestedAt",
                table: "ArchiveItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisposeRequestedBy",
                table: "ArchiveItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDisposeCancelled",
                table: "ArchiveItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOnLegalHold",
                table: "ArchiveItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LegalHoldAt",
                table: "ArchiveItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "ArchiveItems",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArchiveChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SlaHours = table.Column<int>(type: "integer", nullable: false),
                    EvidenceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveChecklists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustodyEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EvidenceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustodyEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustodyEvents_ArchiveItems_ArchiveItemId",
                        column: x => x.ArchiveItemId,
                        principalTable: "ArchiveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_ArchiveChecklists_CorrelationId_Step",
                table: "ArchiveChecklists",
                columns: new[] { "CorrelationId", "Step" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustodyEvents_ArchiveItemId_OccurredAt",
                table: "CustodyEvents",
                columns: new[] { "ArchiveItemId", "OccurredAt" });

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
                name: "ArchiveChecklists");

            migrationBuilder.DropTable(
                name: "CustodyEvents");

            migrationBuilder.DropTable(
                name: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "RetentionCode",
                table: "DocumentClassifications");

            migrationBuilder.DropColumn(
                name: "ApprovalEvidenceFileId",
                table: "DisposalRecords");

            migrationBuilder.DropColumn(
                name: "CompletenessAttestation",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "DisposeApprovedAt",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "DisposeApprovedBy",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "DisposeCancelReason",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "DisposeRequestedAt",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "DisposeRequestedBy",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "IsDisposeCancelled",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "IsOnLegalHold",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "LegalHoldAt",
                table: "ArchiveItems");

            migrationBuilder.DropColumn(
                name: "LegalHoldReason",
                table: "ArchiveItems");
        }
    }
}
