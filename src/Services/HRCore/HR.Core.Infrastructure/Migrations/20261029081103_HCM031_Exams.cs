using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM031_Exams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExamSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Venue = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    MaxScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MarkingClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamSessions", x => x.Id);
                    table.CheckConstraint("CK_ExamSessions_Capacity", "\"Capacity\" >= 1");
                    table.CheckConstraint("CK_ExamSessions_MaxScore", "\"MaxScore\" > 0");
                    table.ForeignKey(
                        name: "FK_ExamSessions_RecruitmentCompetitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "RecruitmentCompetitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SeatNumber = table.Column<int>(type: "integer", nullable: false),
                    Attendance = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FinalMark = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: true),
                    AdmissionCardSha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    AdmissionCardIssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamCandidates_ExamSessions_ExamSessionId",
                        column: x => x.ExamSessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamCandidates_RecruitmentApplications_RecruitmentApplicati~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExamMarkerAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    MarkerEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HoldsHrReadAccess = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamMarkerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamMarkerAssignments_Employees_MarkerEmployeeId",
                        column: x => x.MarkerEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExamMarkerAssignments_ExamSessions_ExamSessionId",
                        column: x => x.ExamSessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExamScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamCandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarkerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Round = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecruitmentAppealId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExamScores_ExamCandidates_ExamCandidateId",
                        column: x => x.ExamCandidateId,
                        principalTable: "ExamCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_CompetitionId_ExamType_ExamNumber",
                table: "ExamCandidates",
                columns: new[] { "CompetitionId", "ExamType", "ExamNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_CompetitionId_ExamType_RecruitmentApplicatio~",
                table: "ExamCandidates",
                columns: new[] { "CompetitionId", "ExamType", "RecruitmentApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_ExamSessionId_SeatNumber",
                table: "ExamCandidates",
                columns: new[] { "ExamSessionId", "SeatNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamCandidates_RecruitmentApplicationId",
                table: "ExamCandidates",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarkerAssignments_ExamSessionId_MarkerEmployeeId",
                table: "ExamMarkerAssignments",
                columns: new[] { "ExamSessionId", "MarkerEmployeeId" },
                unique: true,
                filter: "\"MarkerEmployeeId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarkerAssignments_ExamSessionId_Role",
                table: "ExamMarkerAssignments",
                columns: new[] { "ExamSessionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarkerAssignments_MarkerEmployeeId",
                table: "ExamMarkerAssignments",
                column: "MarkerEmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarkerAssignments_TenantId_MarkerEmployeeId",
                table: "ExamMarkerAssignments",
                columns: new[] { "TenantId", "MarkerEmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamMarkerAssignments_TenantId_MarkerUserId",
                table: "ExamMarkerAssignments",
                columns: new[] { "TenantId", "MarkerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamScores_ExamCandidateId_Round",
                table: "ExamScores",
                columns: new[] { "ExamCandidateId", "Round" },
                unique: true,
                filter: "\"Round\" <> 'ObjectionCorrection'");

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_CompetitionId_Type_ScheduledAt",
                table: "ExamSessions",
                columns: new[] { "CompetitionId", "Type", "ScheduledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExamMarkerAssignments");

            migrationBuilder.DropTable(
                name: "ExamScores");

            migrationBuilder.DropTable(
                name: "ExamCandidates");

            migrationBuilder.DropTable(
                name: "ExamSessions");
        }
    }
}
