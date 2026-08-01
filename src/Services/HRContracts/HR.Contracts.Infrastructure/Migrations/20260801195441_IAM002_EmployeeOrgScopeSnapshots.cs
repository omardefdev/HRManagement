using System;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IAM002_EmployeeOrgScopeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvanceInstallmentPlans_AdvanceRequests_AdvanceRequestId",
                table: "AdvanceInstallmentPlans");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_LeaveType",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeaveBalances_EmployeeId_LeaveType_Year",
                table: "LeaveBalances");

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Contracts",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "AdvanceRequests",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "EmployeeOrgUnitSnapshots",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOrgUnitSnapshots", x => x.EmployeeId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_Tenant_Employee_Status",
                table: "LeaveRequests",
                columns: new[] { "TenantId", "EmployeeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_TenantId_LeaveType",
                table: "LeavePolicies",
                columns: new[] { "TenantId", "LeaveType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalances_TenantId_EmployeeId_LeaveType_Year",
                table: "LeaveBalances",
                columns: new[] { "TenantId", "EmployeeId", "LeaveType", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_OneActivePerEmployee",
                table: "Contracts",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOrgUnitSnapshots_PrimaryOrgUnitId",
                table: "EmployeeOrgUnitSnapshots",
                column: "PrimaryOrgUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_AdvanceInstallmentPlans_AdvanceRequests_AdvanceRequestId",
                table: "AdvanceInstallmentPlans",
                column: "AdvanceRequestId",
                principalTable: "AdvanceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvanceInstallmentPlans_AdvanceRequests_AdvanceRequestId",
                table: "AdvanceInstallmentPlans");

            migrationBuilder.DropTable(
                name: "EmployeeOrgUnitSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequests_Tenant_Employee_Status",
                table: "LeaveRequests");

            migrationBuilder.DropIndex(
                name: "IX_LeavePolicies_TenantId_LeaveType",
                table: "LeavePolicies");

            migrationBuilder.DropIndex(
                name: "IX_LeaveBalances_TenantId_EmployeeId_LeaveType_Year",
                table: "LeaveBalances");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_OneActivePerEmployee",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "AdvanceRequests");

            migrationBuilder.CreateIndex(
                name: "IX_LeavePolicies_LeaveType",
                table: "LeavePolicies",
                column: "LeaveType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveBalances_EmployeeId_LeaveType_Year",
                table: "LeaveBalances",
                columns: new[] { "EmployeeId", "LeaveType", "Year" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AdvanceInstallmentPlans_AdvanceRequests_AdvanceRequestId",
                table: "AdvanceInstallmentPlans",
                column: "AdvanceRequestId",
                principalTable: "AdvanceRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
