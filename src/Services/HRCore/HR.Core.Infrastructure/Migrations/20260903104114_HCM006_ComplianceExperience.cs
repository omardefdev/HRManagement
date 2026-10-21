using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM006_ComplianceExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnonymizedAt",
                table: "RecruitmentApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppealStatus",
                table: "RecruitmentApplications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "LegalHold",
                table: "RecruitmentApplications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NationalIdHash",
                table: "RecruitmentApplications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AnonymizedAt",
                table: "CitizenApplications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LegalHold",
                table: "CitizenApplications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NationalIdHash",
                table: "CitizenApplications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecruitmentAppeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Decision = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IndependentReviewerId = table.Column<Guid>(type: "uuid", nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentAppeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentAppeals_RecruitmentApplications_RecruitmentAppli~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecruitmentInterviewSlots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecruitmentApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CitizenApplicationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScheduledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PanelNotes = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentInterviewSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentInterviewSlots_CitizenApplications_CitizenApplic~",
                        column: x => x.CitizenApplicationId,
                        principalTable: "CitizenApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecruitmentInterviewSlots_RecruitmentApplications_Recruitme~",
                        column: x => x.RecruitmentApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SelectionCommitteeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommitteeDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Findings = table.Column<string>(type: "text", nullable: true),
                    Recommendation = table.Column<string>(type: "text", nullable: true),
                    WorkflowInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelectionCommitteeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeSessions_RecruitmentRequisitions_Requisit~",
                        column: x => x.RequisitionId,
                        principalTable: "RecruitmentRequisitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SelectionCommitteeMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    HasConflictOfInterest = table.Column<bool>(type: "boolean", nullable: false),
                    Recused = table.Column<bool>(type: "boolean", nullable: false),
                    Vote = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelectionCommitteeMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeMembers_SelectionCommitteeSessions_Sessio~",
                        column: x => x.SessionId,
                        principalTable: "SelectionCommitteeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitizenApplications_TenantId_NationalIdHash_RequisitionId",
                table: "CitizenApplications",
                columns: new[] { "TenantId", "NationalIdHash", "RequisitionId" },
                unique: true,
                filter: "\"RequisitionId\" IS NOT NULL AND \"NationalIdHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAppeals_RecruitmentApplicationId",
                table: "RecruitmentAppeals",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentAppeals_TenantId_RecruitmentApplicationId_Status",
                table: "RecruitmentAppeals",
                columns: new[] { "TenantId", "RecruitmentApplicationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentInterviewSlots_CitizenApplicationId",
                table: "RecruitmentInterviewSlots",
                column: "CitizenApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentInterviewSlots_RecruitmentApplicationId",
                table: "RecruitmentInterviewSlots",
                column: "RecruitmentApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentInterviewSlots_TenantId_RecruitmentApplicationId~",
                table: "RecruitmentInterviewSlots",
                columns: new[] { "TenantId", "RecruitmentApplicationId", "ScheduledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeMembers_EmployeeId",
                table: "SelectionCommitteeMembers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeMembers_SessionId",
                table: "SelectionCommitteeMembers",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeMembers_TenantId_SessionId_EmployeeId",
                table: "SelectionCommitteeMembers",
                columns: new[] { "TenantId", "SessionId", "EmployeeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeSessions_RequisitionId",
                table: "SelectionCommitteeSessions",
                column: "RequisitionId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeSessions_TenantId_RequisitionId_Committee~",
                table: "SelectionCommitteeSessions",
                columns: new[] { "TenantId", "RequisitionId", "CommitteeDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitmentAppeals");

            migrationBuilder.DropTable(
                name: "RecruitmentInterviewSlots");

            migrationBuilder.DropTable(
                name: "SelectionCommitteeMembers");

            migrationBuilder.DropTable(
                name: "SelectionCommitteeSessions");

            migrationBuilder.DropIndex(
                name: "IX_CitizenApplications_TenantId_NationalIdHash_RequisitionId",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "AnonymizedAt",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "AppealStatus",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "LegalHold",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "NationalIdHash",
                table: "RecruitmentApplications");

            migrationBuilder.DropColumn(
                name: "AnonymizedAt",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "LegalHold",
                table: "CitizenApplications");

            migrationBuilder.DropColumn(
                name: "NationalIdHash",
                table: "CitizenApplications");
        }
    }
}
