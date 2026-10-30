using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM004_SelectionOfferPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveOfferLetterId",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppealNotes",
                table: "RecruitmentApplications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AssessmentScore",
                table: "RecruitmentApplications",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConflictOfInterestDeclared",
                table: "RecruitmentApplications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConflictOfInterestDeclaredAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CriteriaVersionId",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rank",
                table: "RecruitmentApplications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RankedAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScreenedAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConflictOfInterestDeclared",
                table: "CitizenApplications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConflictOfInterestDeclaredAt",
                table: "CitizenApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OfferLetters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FundedPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IssuedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OfferedSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    GradeCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ProposedStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsConvertedToEmployee = table.Column<bool>(type: "boolean", nullable: false),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferLetters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfferLetters_FundedPositions_FundedPositionId",
                        column: x => x.FundedPositionId,
                        principalTable: "FundedPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfferLetters_RecruitmentApplications_RecruitmentApplication~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecruitmentAssessmentCriteria",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CriteriaJson = table.Column<string>(type: "text", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentAssessmentCriteria", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentAssessmentCriteria_RecruitmentRequisitions_Requi~",
                        column: x => x.RequisitionId,
                        principalTable: "RecruitmentRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_ActiveOfferLetterId",
                table: "RecruitmentApplications",
                column: "ActiveOfferLetterId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_CriteriaVersionId",
                table: "RecruitmentApplications",
                column: "CriteriaVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_TenantId_RequisitionId_Rank",
                table: "RecruitmentApplications",
                columns: new[] { "TenantId", "RequisitionId", "Rank" },
                unique: true,
                filter: "\"Rank\" IS NOT NULL AND \"RequisitionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OfferLetters_FundedPositionId",
                table: "OfferLetters",
                column: "FundedPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferLetters_RecruitmentApplicationId",
                table: "OfferLetters",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferLetters_TenantId_OfferNumber",
                table: "OfferLetters",
                columns: new[] { "TenantId", "OfferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferLetters_TenantId_RecruitmentApplicationId_Status",
                table: "OfferLetters",
                columns: new[] { "TenantId", "RecruitmentApplicationId", "Status" },
                unique: true,
                filter: "\"Status\" IN (2, 3)");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAssessmentCriteria_RequisitionId",
                table: "RecruitmentAssessmentCriteria",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAssessmentCriteria_TenantId_RequisitionId_Versio~",
                table: "RecruitmentAssessmentCriteria",
                columns: new[] { "TenantId", "RequisitionId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentApplications_OfferLetters_ActiveOfferLetterId",
                table: "RecruitmentApplications",
                column: "ActiveOfferLetterId",
                principalTable: "OfferLetters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentApplications_RecruitmentAssessmentCriteria_Crite~",
                table: "RecruitmentApplications",
                column: "CriteriaVersionId",
                principalTable: "RecruitmentAssessmentCriteria",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentApplications_OfferLetters_ActiveOfferLetterId",
                table: "RecruitmentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentApplications_RecruitmentAssessmentCriteria_Crite~",
                table: "RecruitmentApplications");

            migrationBuilder.DropTable(
                name: "OfferLetters");

            migrationBuilder.DropTable(
                name: "RecruitmentAssessmentCriteria");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_ActiveOfferLetterId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_CriteriaVersionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_TenantId_RequisitionId_Rank",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ActiveOfferLetterId",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "AppealNotes",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "AssessmentScore",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ConflictOfInterestDeclared",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ConflictOfInterestDeclaredAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "CriteriaVersionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "Rank",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "RankedAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ScreenedAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "ConflictOfInterestDeclared",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "ConflictOfInterestDeclaredAt",
                table: "CitizenApplications");
        }
    }
}
