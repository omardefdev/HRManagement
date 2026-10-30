using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM001_RecruitmentRequisitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecruitmentRequisitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RequisitionNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FundedPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ApprovedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JustificationNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VacancyCount = table.Column<int>(type: "integer", nullable: false),
                    LegalSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecruitmentRequisitions", x => x.Id);
                    table.CheckConstraint("CK_RecruitmentRequisitions_VacancyCount", "\"VacancyCount\" = 1");
                    table.ForeignKey(
                        name: "FK_RecruitmentRequisitions_FundedPositions_FundedPositionId",
                        column: x => x.FundedPositionId,
                        principalTable: "FundedPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitions_FundedPositionId",
                table: "RecruitmentRequisitions",
                column: "FundedPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitions_TenantId_RequisitionNumber",
                table: "RecruitmentRequisitions",
                columns: new[] { "TenantId", "RequisitionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecruitmentRequisitions_TenantId_Status",
                table: "RecruitmentRequisitions",
                columns: new[] { "TenantId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecruitmentRequisitions");
        }
    }
}
