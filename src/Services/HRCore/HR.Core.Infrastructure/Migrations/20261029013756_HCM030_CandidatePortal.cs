using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM030_CandidatePortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PortalLockedUntil",
                table: "CitizenApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PortalOtpRequestedAt",
                table: "CitizenApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CandidateOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CitizenApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHmac = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateOtpChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CandidateOtpChallenges_CitizenApplications_CitizenApplicati~",
                        column: x => x.CitizenApplicationId,
                        principalTable: "CitizenApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateOtpChallenges_CitizenApplicationId",
                table: "CandidateOtpChallenges",
                column: "CitizenApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CandidateOtpChallenges_TenantId_CitizenApplicationId_Create~",
                table: "CandidateOtpChallenges",
                columns: new[] { "TenantId", "CitizenApplicationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateOtpChallenges");

            migrationBuilder.DropColumn(
                name: "PortalLockedUntil",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "PortalOtpRequestedAt",
                table: "CitizenApplications");
        }
    }
}
