using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compliance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN004_ComplaintProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceFileId",
                table: "DisciplinaryActions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "Complaints",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Complaints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceFileId",
                table: "Complaints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAnonymous",
                table: "Complaints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "Complaints",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ReporterProtectedId",
                table: "Complaints",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InvestigationChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComplaintId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_InvestigationChecklists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvestigationChecklists_ComplaintId_Step",
                table: "InvestigationChecklists",
                columns: new[] { "ComplaintId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestigationChecklists");

            migrationBuilder.DropColumn(
                name: "EvidenceFileId",
                table: "DisciplinaryActions");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "EvidenceFileId",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "IsAnonymous",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "Complaints");

            migrationBuilder.DropColumn(
                name: "ReporterProtectedId",
                table: "Complaints");
        }
    }
}
