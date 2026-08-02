using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IDCard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN005_IdentityAssetsIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesCardId",
                table: "IssuedCards",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RevokeSlaHours",
                table: "IssuedCards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "IssuedCards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UrgentRevoke",
                table: "IssuedCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "CardRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "CardRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "CardRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "IssuedCardId",
                table: "CardRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptFileId",
                table: "CardDeliveryRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CardChecklists",
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
                    table.PrimaryKey("PK_CardChecklists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardChecklists_CorrelationId_Step",
                table: "CardChecklists",
                columns: new[] { "CorrelationId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardChecklists");

            migrationBuilder.DropColumn(
                name: "ReplacesCardId",
                table: "IssuedCards");

            migrationBuilder.DropColumn(
                name: "RevokeSlaHours",
                table: "IssuedCards");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "IssuedCards");

            migrationBuilder.DropColumn(
                name: "UrgentRevoke",
                table: "IssuedCards");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "IssuedCardId",
                table: "CardRequests");

            migrationBuilder.DropColumn(
                name: "ReceiptFileId",
                table: "CardDeliveryRecords");
        }
    }
}
