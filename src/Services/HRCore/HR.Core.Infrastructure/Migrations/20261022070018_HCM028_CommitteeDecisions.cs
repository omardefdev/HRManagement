using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM028_CommitteeDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HCM-028 (A3): duplicate members cannot exist — IX_SelectionCommitteeMembers_TenantId_SessionId_EmployeeId
            // has been unique since the committee tables were created, so no duplicate pre-check is needed here.
            // scripts/hcm028-find-duplicate-committee-members.ps1 re-checks that read-only.
            migrationBuilder.AddColumn<Guid>(
                name: "ChairEmployeeId",
                table: "SelectionCommitteeSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "SelectionCommitteeSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "SelectionCommitteeSessions",
                type: "integer",
                nullable: false,
                // Hand-edited from 0: existing sessions are the classic selection committee → Ranking (1),
                // the same default as new sessions.
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "SelectionCommitteeSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresCommitteeApproval",
                table: "RecruitmentRequisitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SelectionCommitteeDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recommendation = table.Column<int>(type: "integer", nullable: false),
                    ProposedRank = table.Column<int>(type: "integer", nullable: true),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelectionCommitteeDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeDecisions_RecruitmentApplications_Applica~",
                        column: x => x.ApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeDecisions_SelectionCommitteeSessions_Sess~",
                        column: x => x.SessionId,
                        principalTable: "SelectionCommitteeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SelectionCommitteeMemberCois",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    HasConflict = table.Column<bool>(type: "boolean", nullable: false),
                    DeclaredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelectionCommitteeMemberCois", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeMemberCois_RecruitmentApplications_Applic~",
                        column: x => x.ApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeMemberCois_SelectionCommitteeMembers_Memb~",
                        column: x => x.MemberId,
                        principalTable: "SelectionCommitteeMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SelectionCommitteeVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vote = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelectionCommitteeVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeVotes_RecruitmentApplications_Application~",
                        column: x => x.ApplicationId,
                        principalTable: "RecruitmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeVotes_SelectionCommitteeMembers_MemberId",
                        column: x => x.MemberId,
                        principalTable: "SelectionCommitteeMembers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SelectionCommitteeVotes_SelectionCommitteeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "SelectionCommitteeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeDecisions_ApplicationId",
                table: "SelectionCommitteeDecisions",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeDecisions_SessionId_ApplicationId",
                table: "SelectionCommitteeDecisions",
                columns: new[] { "SessionId", "ApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeMemberCois_ApplicationId",
                table: "SelectionCommitteeMemberCois",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeMemberCois_MemberId_ApplicationId",
                table: "SelectionCommitteeMemberCois",
                columns: new[] { "MemberId", "ApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeVotes_ApplicationId",
                table: "SelectionCommitteeVotes",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeVotes_MemberId_ApplicationId",
                table: "SelectionCommitteeVotes",
                columns: new[] { "MemberId", "ApplicationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeVotes_SessionId",
                table: "SelectionCommitteeVotes",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_SelectionCommitteeVotes_TenantId_SessionId",
                table: "SelectionCommitteeVotes",
                columns: new[] { "TenantId", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelectionCommitteeDecisions");

            migrationBuilder.DropTable(
                name: "SelectionCommitteeMemberCois");

            migrationBuilder.DropTable(
                name: "SelectionCommitteeVotes");

            migrationBuilder.DropColumn(
                name: "ChairEmployeeId",
                table: "SelectionCommitteeSessions");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "SelectionCommitteeSessions");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "SelectionCommitteeSessions");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "SelectionCommitteeSessions");

            migrationBuilder.DropColumn(
                name: "RequiresCommitteeApproval",
                table: "RecruitmentRequisitions");
        }
    }
}
