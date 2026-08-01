using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DfdGapClosure_LoanAssignmentAndDistribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LoanAssignmentRequestId",
                table: "MovementExecutionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferRequestId",
                table: "DistributionPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LoanAssignmentRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    LendingOrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    BorrowingOrgUnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Terms = table.Column<string>(type: "text", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoanAssignmentRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovementExecutionRecords_LoanAssignmentRequestId",
                table: "MovementExecutionRecords",
                column: "LoanAssignmentRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_DistributionPlans_TransferRequestId",
                table: "DistributionPlans",
                column: "TransferRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_DistributionPlans_TransferRequests_TransferRequestId",
                table: "DistributionPlans",
                column: "TransferRequestId",
                principalTable: "TransferRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MovementExecutionRecords_LoanAssignmentRequests_LoanAssignm~",
                table: "MovementExecutionRecords",
                column: "LoanAssignmentRequestId",
                principalTable: "LoanAssignmentRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DistributionPlans_TransferRequests_TransferRequestId",
                table: "DistributionPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_MovementExecutionRecords_LoanAssignmentRequests_LoanAssignm~",
                table: "MovementExecutionRecords");

            migrationBuilder.DropTable(
                name: "LoanAssignmentRequests");

            migrationBuilder.DropIndex(
                name: "IX_MovementExecutionRecords_LoanAssignmentRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropIndex(
                name: "IX_DistributionPlans_TransferRequestId",
                table: "DistributionPlans");

            migrationBuilder.DropColumn(
                name: "LoanAssignmentRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropColumn(
                name: "TransferRequestId",
                table: "DistributionPlans");
        }
    }
}
