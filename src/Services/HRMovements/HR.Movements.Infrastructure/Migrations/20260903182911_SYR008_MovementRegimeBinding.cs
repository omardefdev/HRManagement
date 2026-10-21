using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR008_MovementRegimeBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConditionCode",
                table: "TransferSagaStates",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictNotes",
                table: "TransferSagaStates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLegalRegimeId",
                table: "TransferSagaStates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetLegalRegimeId",
                table: "TransferSagaStates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransitionDecisionRef",
                table: "TransferSagaStates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConditionCode",
                table: "TransferRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictNotes",
                table: "TransferRequests",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLegalRegimeId",
                table: "TransferRequests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetLegalRegimeId",
                table: "TransferRequests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransitionDecisionRef",
                table: "TransferRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionCode",
                table: "TransferSagaStates");

            migrationBuilder.DropColumn(
                name: "ConflictNotes",
                table: "TransferSagaStates");

            migrationBuilder.DropColumn(
                name: "SourceLegalRegimeId",
                table: "TransferSagaStates");

            migrationBuilder.DropColumn(
                name: "TargetLegalRegimeId",
                table: "TransferSagaStates");

            migrationBuilder.DropColumn(
                name: "TransitionDecisionRef",
                table: "TransferSagaStates");

            migrationBuilder.DropColumn(
                name: "ConditionCode",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "ConflictNotes",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "SourceLegalRegimeId",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "TargetLegalRegimeId",
                table: "TransferRequests");

            migrationBuilder.DropColumn(
                name: "TransitionDecisionRef",
                table: "TransferRequests");
        }
    }
}
