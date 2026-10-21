using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Compliance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM006_CandidateDsar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubjectCitizenApplicationId",
                table: "DsarRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DsarRequests_TenantId_SubjectCitizenApplicationId",
                table: "DsarRequests",
                columns: new[] { "TenantId", "SubjectCitizenApplicationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DsarRequests_TenantId_SubjectCitizenApplicationId",
                table: "DsarRequests");

            migrationBuilder.DropColumn(
                name: "SubjectCitizenApplicationId",
                table: "DsarRequests");
        }
    }
}
