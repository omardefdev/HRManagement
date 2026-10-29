using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM031_Results : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompetitionResultVersionId",
                table: "RecruitmentAppeals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "RecruitmentAppeals",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Rejection");

            migrationBuilder.AddColumn<decimal>(
                name: "ObjectionCorrectedScore",
                table: "RecruitmentAppeals",
                type: "numeric(7,2)",
                precision: 7,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ObjectionExamCandidateId",
                table: "RecruitmentAppeals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompetitionAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocatedLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocatedRequisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreferenceOrderSatisfied = table.Column<int>(type: "integer", nullable: true),
                    FromReserve = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionAllocations_CompetitionLines_AllocatedLineId",
                        column: x => x.AllocatedLineId,
                        principalTable: "CompetitionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionAllocations_RecruitmentApplications_RecruitmentA~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionAllocations_RecruitmentCompetitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "RecruitmentCompetitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionAllocations_RecruitmentRequisitions_AllocatedReq~",
                        column: x => x.AllocatedRequisitionId,
                        principalTable: "RecruitmentRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionReserveEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineRank = table.Column<int>(type: "integer", nullable: false),
                    ReserveUntil = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StatusChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RecalledRequisitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionReserveEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionReserveEntries_CompetitionLines_CompetitionLineId",
                        column: x => x.CompetitionLineId,
                        principalTable: "CompetitionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionReserveEntries_RecruitmentApplications_Recruitme~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionResultVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ComputedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ReturnReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DiffJson = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionResultVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionResultVersions_RecruitmentCompetitions_Competiti~",
                        column: x => x.CompetitionId,
                        principalTable: "RecruitmentCompetitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExamScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    InterviewScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    FinalScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    OverallRank = table.Column<int>(type: "integer", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TieUnresolved = table.Column<bool>(type: "boolean", nullable: false),
                    TieDecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionResults_CompetitionResultVersions_ResultVersionId",
                        column: x => x.ResultVersionId,
                        principalTable: "CompetitionResultVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionResults_RecruitmentApplications_RecruitmentAppli~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentCompetitions_CurrentResultVersionId",
                table: "RecruitmentCompetitions",
                column: "CurrentResultVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAppeals_CompetitionResultVersionId",
                table: "RecruitmentAppeals",
                column: "CompetitionResultVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAppeals_ObjectionExamCandidateId",
                table: "RecruitmentAppeals",
                column: "ObjectionExamCandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionAllocations_AllocatedLineId",
                table: "CompetitionAllocations",
                column: "AllocatedLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionAllocations_AllocatedRequisitionId",
                table: "CompetitionAllocations",
                column: "AllocatedRequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionAllocations_CompetitionId_RecruitmentApplication~",
                table: "CompetitionAllocations",
                columns: new[] { "CompetitionId", "RecruitmentApplicationId" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionAllocations_RecruitmentApplicationId",
                table: "CompetitionAllocations",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionReserveEntries_CompetitionLineId_RecruitmentAppl~",
                table: "CompetitionReserveEntries",
                columns: new[] { "CompetitionLineId", "RecruitmentApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionReserveEntries_CompetitionLineId_Status_LineRank",
                table: "CompetitionReserveEntries",
                columns: new[] { "CompetitionLineId", "Status", "LineRank" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionReserveEntries_RecruitmentApplicationId",
                table: "CompetitionReserveEntries",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionResults_RecruitmentApplicationId",
                table: "CompetitionResults",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionResults_ResultVersionId_ExamNumber",
                table: "CompetitionResults",
                columns: new[] { "ResultVersionId", "ExamNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionResults_ResultVersionId_RecruitmentApplicationId",
                table: "CompetitionResults",
                columns: new[] { "ResultVersionId", "RecruitmentApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionResultVersions_CompetitionId_VersionNo",
                table: "CompetitionResultVersions",
                columns: new[] { "CompetitionId", "VersionNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionResultVersions_OnePublished",
                table: "CompetitionResultVersions",
                column: "CompetitionId",
                unique: true,
                filter: "\"Status\" = 'Published'");

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentAppeals_CompetitionResultVersions_CompetitionRes~",
                table: "RecruitmentAppeals",
                column: "CompetitionResultVersionId",
                principalTable: "CompetitionResultVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentAppeals_ExamCandidates_ObjectionExamCandidateId",
                table: "RecruitmentAppeals",
                column: "ObjectionExamCandidateId",
                principalTable: "ExamCandidates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentCompetitions_CompetitionResultVersions_CurrentRe~",
                table: "RecruitmentCompetitions",
                column: "CurrentResultVersionId",
                principalTable: "CompetitionResultVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentAppeals_CompetitionResultVersions_CompetitionRes~",
                table: "RecruitmentAppeals");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentAppeals_ExamCandidates_ObjectionExamCandidateId",
                table: "RecruitmentAppeals");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentCompetitions_CompetitionResultVersions_CurrentRe~",
                table: "RecruitmentCompetitions");

            migrationBuilder.DropTable(
                name: "CompetitionAllocations");

            migrationBuilder.DropTable(
                name: "CompetitionReserveEntries");

            migrationBuilder.DropTable(
                name: "CompetitionResults");

            migrationBuilder.DropTable(
                name: "CompetitionResultVersions");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentCompetitions_CurrentResultVersionId",
                table: "RecruitmentCompetitions");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentAppeals_CompetitionResultVersionId",
                table: "RecruitmentAppeals");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentAppeals_ObjectionExamCandidateId",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "CompetitionResultVersionId",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "ObjectionCorrectedScore",
                table: "RecruitmentAppeals");

            migrationBuilder.DropColumn(
                name: "ObjectionExamCandidateId",
                table: "RecruitmentAppeals");
        }
    }
}
