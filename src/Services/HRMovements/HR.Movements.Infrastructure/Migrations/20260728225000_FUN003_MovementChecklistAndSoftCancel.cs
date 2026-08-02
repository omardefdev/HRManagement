using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN003_MovementChecklistAndSoftCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "TransferRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "TransferRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EffectiveDate",
                table: "TransferRequests",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "TransferRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                table: "SecondmentSagaStates",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "HostOrg",
                table: "SecondmentSagaStates",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "HostOrg",
                table: "SecondmentRequests",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "SecondmentRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "SecondmentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "SecondmentRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedBy",
                table: "SecondmentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "LoanAssignmentRequests",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "LoanAssignmentRequests",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "LoanAssignmentRequests",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedBy",
                table: "LoanAssignmentRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MovementChecklists",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementKind = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedByName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OwnerRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SlaHours = table.Column<int>(type: "integer", nullable: false),
                    EvidenceRef = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovementChecklists", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovementChecklists_CorrelationId",
                table: "MovementChecklists",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_MovementChecklists_MovementKind_CorrelationId_Step",
                table: "MovementChecklists",
                columns: new[] { "MovementKind", "CorrelationId", "Step" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovementChecklists");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "EffectiveDate",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "SecondmentSagaStates");

            migrationBuilder.DropColumn(
                name: "HostOrg",
                table: "SecondmentSagaStates");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "SecondmentRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "SecondmentRequests");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "SecondmentRequests");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                table: "SecondmentRequests");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "LoanAssignmentRequests");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "LoanAssignmentRequests");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "LoanAssignmentRequests");

            migrationBuilder.DropColumn(
                name: "RequestedBy",
                table: "LoanAssignmentRequests");

            migrationBuilder.AlterColumn<string>(
                name: "HostOrg",
                table: "SecondmentRequests",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);
        }
    }
}
