using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSuccessionAndCareerPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CareerPaths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerPaths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeyPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrgUnitId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentHolderEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Criticality = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeyPositions_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CareerPathSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CareerPathId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    PositionTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MinYears = table.Column<int>(type: "integer", nullable: false),
                    Requirements = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CareerPathSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CareerPathSteps_CareerPaths_CareerPathId",
                        column: x => x.CareerPathId,
                        principalTable: "CareerPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuccessionPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyPositionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuccessionPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuccessionPlans_KeyPositions_KeyPositionId",
                        column: x => x.KeyPositionId,
                        principalTable: "KeyPositions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SuccessorCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SuccessionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LatestEvaluationScore = table.Column<decimal>(type: "numeric", nullable: false),
                    YearsInService = table.Column<int>(type: "integer", nullable: false),
                    MeetsQualification = table.Column<bool>(type: "boolean", nullable: false),
                    Readiness = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuccessorCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SuccessorCandidates_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SuccessorCandidates_SuccessionPlans_SuccessionPlanId",
                        column: x => x.SuccessionPlanId,
                        principalTable: "SuccessionPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CareerPathSteps_CareerPathId",
                table: "CareerPathSteps",
                column: "CareerPathId");

            migrationBuilder.CreateIndex(
                name: "IX_KeyPositions_OrgUnitId",
                table: "KeyPositions",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SuccessionPlans_KeyPositionId",
                table: "SuccessionPlans",
                column: "KeyPositionId");

            migrationBuilder.CreateIndex(
                name: "IX_SuccessorCandidates_EmployeeId",
                table: "SuccessorCandidates",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_SuccessorCandidates_SuccessionPlanId",
                table: "SuccessorCandidates",
                column: "SuccessionPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CareerPathSteps");

            migrationBuilder.DropTable(
                name: "SuccessorCandidates");

            migrationBuilder.DropTable(
                name: "CareerPaths");

            migrationBuilder.DropTable(
                name: "SuccessionPlans");

            migrationBuilder.DropTable(
                name: "KeyPositions");
        }
    }
}
