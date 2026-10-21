using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM026_AssessmentScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecruitmentAssessmentScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CriteriaVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AssessorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssessorEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    NoConflictDeclared = table.Column<bool>(type: "boolean", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentAssessmentScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentAssessmentScores_RecruitmentApplications_Recruit~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecruitmentAssessmentScores_RecruitmentAssessmentCriteria_C~",
                        column: x => x.CriteriaVersionId,
                        principalTable: "RecruitmentAssessmentCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAssessmentScores_CriteriaVersionId",
                table: "RecruitmentAssessmentScores",
                column: "CriteriaVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAssessmentScores_RecruitmentApplicationId",
                table: "RecruitmentAssessmentScores",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAssessmentScores_TenantId_RecruitmentApplication~",
                table: "RecruitmentAssessmentScores",
                columns: new[] { "TenantId", "RecruitmentApplicationId", "AssessorUserId", "ItemKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitmentAssessmentScores");
        }
    }
}
