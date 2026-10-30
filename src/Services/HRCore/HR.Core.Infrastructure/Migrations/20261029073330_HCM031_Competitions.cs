using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM031_Competitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompetitionLineId",
                table: "RecruitmentRequisitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompetitionId",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompetitionLineId",
                table: "RecruitmentApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecruitmentCompetitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApplicationConditions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PublishingChannel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PublicJobPostingId = table.Column<Guid>(type: "uuid", nullable: true),
                    InternalVacancyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AnnouncementDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ApplicationClosingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AnnouncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntakeClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObjectionWindowDays = table.Column<int>(type: "integer", nullable: false),
                    ReserveValidityMonths = table.Column<int>(type: "integer", nullable: false),
                    MaxLinesPerCandidate = table.Column<int>(type: "integer", nullable: false),
                    PassMark = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    ReconcileThreshold = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    ReconcileRule = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TieBreakOrder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ApprovalWorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReturnReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CurrentResultVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultsFirstPublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentCompetitions", x => x.Id);
                    table.CheckConstraint("CK_RecruitmentCompetitions_MaxLinesPerCandidate", "\"MaxLinesPerCandidate\" >= 1");
                    table.CheckConstraint("CK_RecruitmentCompetitions_ObjectionWindowDays", "\"ObjectionWindowDays\" >= 1 AND \"ObjectionWindowDays\" <= 60");
                    table.CheckConstraint("CK_RecruitmentCompetitions_ReserveValidityMonths", "\"ReserveValidityMonths\" >= 0 AND \"ReserveValidityMonths\" <= 36");
                    table.ForeignKey(
                        name: "FK_RecruitmentCompetitions_InternalVacancies_InternalVacancyId",
                        column: x => x.InternalVacancyId,
                        principalTable: "InternalVacancies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecruitmentCompetitions_LegislationSources_LegalSourceId",
                        column: x => x.LegalSourceId,
                        principalTable: "LegislationSources",
                        principalColumn: "LegalSourceId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecruitmentCompetitions_PublicJobPostings_PublicJobPostingId",
                        column: x => x.PublicJobPostingId,
                        principalTable: "PublicJobPostings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNo = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GradeCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    JobCatalogEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriteriaVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequiredCount = table.Column<int>(type: "integer", nullable: false),
                    LinePassMark = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionLines_JobCatalogEntries_JobCatalogEntryId",
                        column: x => x.JobCatalogEntryId,
                        principalTable: "JobCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionLines_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionLines_RecruitmentAssessmentCriteria_CriteriaVers~",
                        column: x => x.CriteriaVersionId,
                        principalTable: "RecruitmentAssessmentCriteria",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionLines_RecruitmentCompetitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "RecruitmentCompetitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionLineId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    EligibilityFlags = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionPreferences", x => x.Id);
                    table.CheckConstraint("CK_CompetitionPreferences_Order", "\"Order\" >= 1");
                    table.ForeignKey(
                        name: "FK_CompetitionPreferences_CompetitionLines_CompetitionLineId",
                        column: x => x.CompetitionLineId,
                        principalTable: "CompetitionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionPreferences_RecruitmentApplications_RecruitmentA~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitions_CompetitionLineId",
                table: "RecruitmentRequisitions",
                column: "CompetitionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_CompetitionId",
                table: "RecruitmentApplications",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_CompetitionLineId",
                table: "RecruitmentApplications",
                column: "CompetitionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_TenantId_CompetitionId_NationalIdHa~",
                table: "RecruitmentApplications",
                columns: new[] { "TenantId", "CompetitionId", "NationalIdHash" },
                unique: true,
                filter: "\"CompetitionId\" IS NOT NULL AND \"NationalIdHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentApplications_TenantId_CompetitionId_Status",
                table: "RecruitmentApplications",
                columns: new[] { "TenantId", "CompetitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionLines_CompetitionId_LineNo",
                table: "CompetitionLines",
                columns: new[] { "CompetitionId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionLines_CriteriaVersionId",
                table: "CompetitionLines",
                column: "CriteriaVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionLines_JobCatalogEntryId",
                table: "CompetitionLines",
                column: "JobCatalogEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionLines_OrgUnitId",
                table: "CompetitionLines",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionPreferences_CompetitionLineId",
                table: "CompetitionPreferences",
                column: "CompetitionLineId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionPreferences_RecruitmentApplicationId_Competition~",
                table: "CompetitionPreferences",
                columns: new[] { "RecruitmentApplicationId", "CompetitionLineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionPreferences_RecruitmentApplicationId_Order",
                table: "CompetitionPreferences",
                columns: new[] { "RecruitmentApplicationId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionPreferences_TenantId_CompetitionLineId",
                table: "CompetitionPreferences",
                columns: new[] { "TenantId", "CompetitionLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentCompetitions_InternalVacancyId",
                table: "RecruitmentCompetitions",
                column: "InternalVacancyId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentCompetitions_LegalSourceId",
                table: "RecruitmentCompetitions",
                column: "LegalSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentCompetitions_PublicJobPostingId",
                table: "RecruitmentCompetitions",
                column: "PublicJobPostingId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentCompetitions_TenantId_Number",
                table: "RecruitmentCompetitions",
                columns: new[] { "TenantId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentCompetitions_TenantId_Status",
                table: "RecruitmentCompetitions",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentApplications_CompetitionLines_CompetitionLineId",
                table: "RecruitmentApplications",
                column: "CompetitionLineId",
                principalTable: "CompetitionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentApplications_RecruitmentCompetitions_Competition~",
                table: "RecruitmentApplications",
                column: "CompetitionId",
                principalTable: "RecruitmentCompetitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentRequisitions_CompetitionLines_CompetitionLineId",
                table: "RecruitmentRequisitions",
                column: "CompetitionLineId",
                principalTable: "CompetitionLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentApplications_CompetitionLines_CompetitionLineId",
                table: "RecruitmentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentApplications_RecruitmentCompetitions_Competition~",
                table: "RecruitmentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentRequisitions_CompetitionLines_CompetitionLineId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropTable(
                name: "CompetitionPreferences");

            migrationBuilder.DropTable(
                name: "CompetitionLines");

            migrationBuilder.DropTable(
                name: "RecruitmentCompetitions");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentRequisitions_CompetitionLineId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_CompetitionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_CompetitionLineId",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_TenantId_CompetitionId_NationalIdHa~",
                table: "RecruitmentApplications");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentApplications_TenantId_CompetitionId_Status",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "CompetitionLineId",
                table: "RecruitmentRequisitions");

            migrationBuilder.DropColumn(
                name: "CompetitionId",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "CompetitionLineId",
                table: "RecruitmentApplications");
        }
    }
}
