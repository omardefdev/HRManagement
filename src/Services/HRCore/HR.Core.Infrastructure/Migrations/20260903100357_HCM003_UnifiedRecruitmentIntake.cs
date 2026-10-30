using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM003_UnifiedRecruitmentIntake : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotedRecruitmentApplicationId",
                table: "CitizenApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "CitizenApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_RequisitionId",
                table: "RecruitmentApplications",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_TenantId_RequisitionId",
                table: "RecruitmentApplications",
                columns: new[] { "TenantId", "RequisitionId" });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_PromotedRecruitmentApplicationId",
                table: "CitizenApplications",
                column: "PromotedRecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_RequisitionId",
                table: "CitizenApplications",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_TenantId_NationalId_RequisitionId",
                table: "CitizenApplications",
                columns: new[] { "TenantId", "NationalId", "RequisitionId" },
                unique: true,
                filter: "\"RequisitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_TenantId_RequisitionId",
                table: "CitizenApplications",
                columns: new[] { "TenantId", "RequisitionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CitizenApplications_RecruitmentApplications_PromotedRecruit~",
                table: "CitizenApplications",
                column: "PromotedRecruitmentApplicationId",
                principalTable: "RecruitmentApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CitizenApplications_RecruitmentRequisitions_RequisitionId",
                table: "CitizenApplications",
                column: "RequisitionId",
                principalTable: "RecruitmentRequisitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentApplications_RecruitmentRequisitions_Requisition~",
                table: "RecruitmentApplications",
                column: "RequisitionId",
                principalTable: "RecruitmentRequisitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CitizenApplications_RecruitmentApplications_PromotedRecruit~",
                table: "CitizenApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_CitizenApplications_RecruitmentRequisitions_RequisitionId",
                table: "CitizenApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentApplications_RecruitmentRequisitions_Requisition~",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_RequisitionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_TenantId_RequisitionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_PromotedRecruitmentApplicationId",
                table: "CitizenApplications");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_RequisitionId",
                table: "CitizenApplications");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_TenantId_NationalId_RequisitionId",
                table: "CitizenApplications");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_TenantId_RequisitionId",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "PromotedRecruitmentApplicationId",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "CitizenApplications");
        }
    }
}
