using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SYR009_Article159Governance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ConflictNotes",
                table: "ServiceRegimeExceptions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictingEntitlementsJson",
                table: "ServiceRegimeExceptions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalDecisionId",
                table: "ServiceRegimeExceptions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictNotes",
                table: "ServiceRegimeExceptions");

            migrationBuilder.DropColumn(
                name: "ConflictingEntitlementsJson",
                table: "ServiceRegimeExceptions");

            migrationBuilder.DropColumn(
                name: "LegalDecisionId",
                table: "ServiceRegimeExceptions");
        }
    }
}
