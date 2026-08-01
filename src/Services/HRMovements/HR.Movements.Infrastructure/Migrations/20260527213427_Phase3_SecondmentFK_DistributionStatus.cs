using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_SecondmentFK_DistributionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SecondmentRequestId",
                table: "MovementExecutionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "DistributionPlans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_MovementExecutionRecords_SecondmentRequestId",
                table: "MovementExecutionRecords",
                column: "SecondmentRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_MovementExecutionRecords_SecondmentRequests_SecondmentReque~",
                table: "MovementExecutionRecords",
                column: "SecondmentRequestId",
                principalTable: "SecondmentRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovementExecutionRecords_SecondmentRequests_SecondmentReque~",
                table: "MovementExecutionRecords");

            migrationBuilder.DropIndex(
                name: "IX_MovementExecutionRecords_SecondmentRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropColumn(
                name: "SecondmentRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "DistributionPlans");
        }
    }
}
