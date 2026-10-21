using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM002_RequisitionLifecyclePostingLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PublicJobPostingId",
                table: "RecruitmentRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAt",
                table: "RecruitmentRequisitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "RecruitmentRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequisitionId",
                table: "PublicJobPostings",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicJobPostings_RequisitionId",
                table: "PublicJobPostings",
                column: "RequisitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicJobPostings_TenantId_RequisitionId",
                table: "PublicJobPostings",
                columns: new[] { "TenantId", "RequisitionId" },
                unique: true,
                filter: "\"RequisitionId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_PublicJobPostings_RecruitmentRequisitions_RequisitionId",
                table: "PublicJobPostings",
                column: "RequisitionId",
                principalTable: "RecruitmentRequisitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PublicJobPostings_RecruitmentRequisitions_RequisitionId",
                table: "PublicJobPostings");

            migrationBuilder.DropIndex(
                name: "IX_PublicJobPostings_RequisitionId",
                table: "PublicJobPostings");

            migrationBuilder.DropIndex(
                name: "IX_PublicJobPostings_TenantId_RequisitionId",
                table: "PublicJobPostings");

            migrationBuilder.DropColumn(
                name: "PublicJobPostingId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "RequisitionId",
                table: "PublicJobPostings");
        }
    }
}
