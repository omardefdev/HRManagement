using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IDCard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardDeliveryRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrintedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PrintedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredTo = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SignatureOrReceipt = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardDeliveryRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardDesigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Template = table.Column<string>(type: "text", nullable: true),
                    ValidityDays = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardDesigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardPermissionSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Permissions = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardPermissionSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfficialSealRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SealImageUrl = table.Column<string>(type: "text", nullable: true),
                    IssuedBy = table.Column<string>(type: "text", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficialSealRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IssuedCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardDesignId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SerialNumber = table.Column<string>(type: "text", nullable: false),
                    DeliveryRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssuedCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IssuedCards_CardDeliveryRecords_DeliveryRecordId",
                        column: x => x.DeliveryRecordId,
                        principalTable: "CardDeliveryRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_IssuedCards_CardDesigns_CardDesignId",
                        column: x => x.CardDesignId,
                        principalTable: "CardDesigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AccessPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedCardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resource = table.Column<string>(type: "text", nullable: false),
                    AccessLevel = table.Column<string>(type: "text", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessPermissions_IssuedCards_IssuedCardId",
                        column: x => x.IssuedCardId,
                        principalTable: "IssuedCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessPermissions_IssuedCardId",
                table: "AccessPermissions",
                column: "IssuedCardId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCards_CardDesignId",
                table: "IssuedCards",
                column: "CardDesignId");

            migrationBuilder.CreateIndex(
                name: "IX_IssuedCards_DeliveryRecordId",
                table: "IssuedCards",
                column: "DeliveryRecordId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessPermissions");

            migrationBuilder.DropTable(
                name: "CardPermissionSets");

            migrationBuilder.DropTable(
                name: "CardRequests");

            migrationBuilder.DropTable(
                name: "OfficialSealRecords");

            migrationBuilder.DropTable(
                name: "IssuedCards");

            migrationBuilder.DropTable(
                name: "CardDeliveryRecords");

            migrationBuilder.DropTable(
                name: "CardDesigns");
        }
    }
}
