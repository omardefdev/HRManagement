using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM007_InternalChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InternalVacancyId",
                table: "RecruitmentRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublishingChannel",
                table: "RecruitmentRequisitions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceInternalApplicationId",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDerivedFromRequisition",
                table: "InternalVacancies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "InternalVacancies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotedRecruitmentApplicationId",
                table: "InternalApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "InternalApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_SourceInternalApplicationId",
                table: "RecruitmentApplications",
                column: "SourceInternalApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalVacancies_RequisitionId",
                table: "InternalVacancies",
                column: "RequisitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InternalVacancies_TenantId_RequisitionId",
                table: "InternalVacancies",
                columns: new[] { "TenantId", "RequisitionId" },
                unique: true,
                filter: "\"RequisitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InternalApplications_PromotedRecruitmentApplicationId",
                table: "InternalApplications",
                column: "PromotedRecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalApplications_RequisitionId",
                table: "InternalApplications",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_InternalApplications_TenantId_EmployeeId_RequisitionId",
                table: "InternalApplications",
                columns: new[] { "TenantId", "EmployeeId", "RequisitionId" },
                unique: true,
                filter: "\"RequisitionId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_InternalApplications_RecruitmentApplications_PromotedRecrui~",
                table: "InternalApplications",
                column: "PromotedRecruitmentApplicationId",
                principalTable: "RecruitmentApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_InternalApplications_RecruitmentRequisitions_RequisitionId",
                table: "InternalApplications",
                column: "RequisitionId",
                principalTable: "RecruitmentRequisitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InternalVacancies_RecruitmentRequisitions_RequisitionId",
                table: "InternalVacancies",
                column: "RequisitionId",
                principalTable: "RecruitmentRequisitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentApplications_InternalApplications_SourceInternal~",
                table: "RecruitmentApplications",
                column: "SourceInternalApplicationId",
                principalTable: "InternalApplications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InternalApplications_RecruitmentApplications_PromotedRecrui~",
                table: "InternalApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_InternalApplications_RecruitmentRequisitions_RequisitionId",
                table: "InternalApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_InternalVacancies_RecruitmentRequisitions_RequisitionId",
                table: "InternalVacancies");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentApplications_InternalApplications_SourceInternal~",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_SourceInternalApplicationId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_InternalVacancies_RequisitionId",
                table: "InternalVacancies");

            migrationBuilder.DropIndex(
                name: "IX_InternalVacancies_TenantId_RequisitionId",
                table: "InternalVacancies");

            migrationBuilder.DropIndex(
                name: "IX_InternalApplications_PromotedRecruitmentApplicationId",
                table: "InternalApplications");

            migrationBuilder.DropIndex(
                name: "IX_InternalApplications_RequisitionId",
                table: "InternalApplications");

            migrationBuilder.DropIndex(
                name: "IX_InternalApplications_TenantId_EmployeeId_RequisitionId",
                table: "InternalApplications");

            migrationBuilder.DropColumn(
                name: "InternalVacancyId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "PublishingChannel",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "SourceInternalApplicationId",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "IsDerivedFromRequisition",
                table: "InternalVacancies");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "InternalVacancies");

            migrationBuilder.DropColumn(
                name: "PromotedRecruitmentApplicationId",
                table: "InternalApplications");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "InternalApplications");
        }
    }
}
