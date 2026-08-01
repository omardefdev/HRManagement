using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compliance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DfdGapClosure_ComplaintFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ComplaintId",
                table: "LegalReferrals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "FindingId",
                table: "ComplianceRecommendations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "ComplaintId",
                table: "ComplianceRecommendations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ChangedFields = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalReferrals_ComplaintId",
                table: "LegalReferrals",
                column: "ComplaintId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceRecommendations_ComplaintId",
                table: "ComplianceRecommendations",
                column: "ComplaintId");

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceRecommendations_Complaints_ComplaintId",
                table: "ComplianceRecommendations",
                column: "ComplaintId",
                principalTable: "Complaints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LegalReferrals_Complaints_ComplaintId",
                table: "LegalReferrals",
                column: "ComplaintId",
                principalTable: "Complaints",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceRecommendations_Complaints_ComplaintId",
                table: "ComplianceRecommendations");

            migrationBuilder.DropForeignKey(
                name: "FK_LegalReferrals_Complaints_ComplaintId",
                table: "LegalReferrals");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_LegalReferrals_ComplaintId",
                table: "LegalReferrals");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceRecommendations_ComplaintId",
                table: "ComplianceRecommendations");

            migrationBuilder.DropColumn(
                name: "ComplaintId",
                table: "LegalReferrals");

            migrationBuilder.DropColumn(
                name: "ComplaintId",
                table: "ComplianceRecommendations");

            migrationBuilder.AlterColumn<Guid>(
                name: "FindingId",
                table: "ComplianceRecommendations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
