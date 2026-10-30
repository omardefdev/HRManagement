using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR009_PromotionDelegationMovement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DelegationRequestId",
                table: "MovementExecutionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PromotionRequestId",
                table: "MovementExecutionRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DelegationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostOrg = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Terms = table.Column<string>(type: "text", nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DelegationRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromotionRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLegalRegimeId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    FromGrade = table.Column<int>(type: "integer", nullable: false),
                    ToGrade = table.Column<int>(type: "integer", nullable: false),
                    FromStep = table.Column<int>(type: "integer", nullable: false),
                    ToStep = table.Column<int>(type: "integer", nullable: false),
                    TransitionDecisionRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovementExecutionRecords_DelegationRequestId",
                table: "MovementExecutionRecords",
                column: "DelegationRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_MovementExecutionRecords_PromotionRequestId",
                table: "MovementExecutionRecords",
                column: "PromotionRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_MovementExecutionRecords_DelegationRequests_DelegationReque~",
                table: "MovementExecutionRecords",
                column: "DelegationRequestId",
                principalTable: "DelegationRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MovementExecutionRecords_PromotionRequests_PromotionRequest~",
                table: "MovementExecutionRecords",
                column: "PromotionRequestId",
                principalTable: "PromotionRequests",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MovementExecutionRecords_DelegationRequests_DelegationReque~",
                table: "MovementExecutionRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_MovementExecutionRecords_PromotionRequests_PromotionRequest~",
                table: "MovementExecutionRecords");

            migrationBuilder.DropTable(
                name: "DelegationRequests");

            migrationBuilder.DropTable(
                name: "PromotionRequests");

            migrationBuilder.DropIndex(
                name: "IX_MovementExecutionRecords_DelegationRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropIndex(
                name: "IX_MovementExecutionRecords_PromotionRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropColumn(
                name: "DelegationRequestId",
                table: "MovementExecutionRecords");

            migrationBuilder.DropColumn(
                name: "PromotionRequestId",
                table: "MovementExecutionRecords");
        }
    }
}
