using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM026_InterviewLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelledReason",
                table: "RecruitmentInterviewSlots",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "RecruitmentInterviewSlots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "RecruitmentInterviewSlots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Outcome",
                table: "RecruitmentInterviewSlots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OutcomeNotes",
                table: "RecruitmentInterviewSlots",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RescheduledFromSlotId",
                table: "RecruitmentInterviewSlots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RecruitmentInterviewPanelMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CoiDeclared = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentInterviewPanelMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecruitmentInterviewPanelMembers_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecruitmentInterviewPanelMembers_RecruitmentInterviewSlots_~",
                        column: x => x.SlotId,
                        principalTable: "RecruitmentInterviewSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentInterviewSlots_RescheduledFromSlotId",
                table: "RecruitmentInterviewSlots",
                column: "RescheduledFromSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentInterviewPanelMembers_EmployeeId",
                table: "RecruitmentInterviewPanelMembers",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentInterviewPanelMembers_SlotId_EmployeeId",
                table: "RecruitmentInterviewPanelMembers",
                columns: new[] { "SlotId", "EmployeeId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecruitmentInterviewSlots_RecruitmentInterviewSlots_Resched~",
                table: "RecruitmentInterviewSlots",
                column: "RescheduledFromSlotId",
                principalTable: "RecruitmentInterviewSlots",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecruitmentInterviewSlots_RecruitmentInterviewSlots_Resched~",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropTable(
                name: "RecruitmentInterviewPanelMembers");

            migrationBuilder.DropIndex(
                name: "IX_RecruitmentInterviewSlots_RescheduledFromSlotId",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropColumn(
                name: "CancelledReason",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropColumn(
                name: "OutcomeNotes",
                table: "RecruitmentInterviewSlots");

            migrationBuilder.DropColumn(
                name: "RescheduledFromSlotId",
                table: "RecruitmentInterviewSlots");
        }
    }
}
