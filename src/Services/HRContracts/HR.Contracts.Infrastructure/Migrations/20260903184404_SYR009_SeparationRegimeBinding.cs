using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR009_SeparationRegimeBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayrollRegimeCode",
                table: "SeparationSagaStates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLegalRegimeId",
                table: "SeparationSagaStates",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayrollRegimeCode",
                table: "SeparationRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceLegalRegimeId",
                table: "SeparationRecords",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayrollRegimeCode",
                table: "SeparationSagaStates");

            migrationBuilder.DropColumn(
                name: "SourceLegalRegimeId",
                table: "SeparationSagaStates");

            migrationBuilder.DropColumn(
                name: "PayrollRegimeCode",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "SourceLegalRegimeId",
                table: "SeparationRecords");
        }
    }
}
