using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_LeaveBalancePolicyInstallmentChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdvanceInstallmentPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvanceRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MonthlyInstallment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    NumberOfInstallments = table.Column<int>(type: "integer", nullable: false),
                    StartMonth = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvanceInstallmentPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvanceInstallmentPlans_AdvanceRequests_AdvanceRequestId",
                        column: x => x.AdvanceRequestId,
                        principalTable: "AdvanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeaveBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TotalEntitlement = table.Column<int>(type: "integer", nullable: false),
                    Used = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LeavePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaxDaysPerYear = table.Column<int>(type: "integer", nullable: false),
                    RequiresMedicalCertificate = table.Column<bool>(type: "boolean", nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false),
                    MinNoticeDays = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeavePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SeparationChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeparationRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeparationChecklists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeparationChecklists_SeparationRecords_SeparationRecordId",
                        column: x => x.SeparationRecordId,
                        principalTable: "SeparationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvanceInstallmentPlans_AdvanceRequestId",
                table: "AdvanceInstallmentPlans",
                column: "AdvanceRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalances_EmployeeId_LeaveType_Year",
                table: "LeaveBalances",
                columns: new[] { "EmployeeId", "LeaveType", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_LeaveType",
                table: "LeavePolicies",
                column: "LeaveType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SeparationChecklists_SeparationRecordId_Step",
                table: "SeparationChecklists",
                columns: new[] { "SeparationRecordId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvanceInstallmentPlans");

            migrationBuilder.DropTable(
                name: "LeaveBalances");

            migrationBuilder.DropTable(
                name: "LeavePolicies");

            migrationBuilder.DropTable(
                name: "SeparationChecklists");
        }
    }
}
