using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeparationEventPublished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SeparationEventPublished",
                table: "SeparationRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeparationEventPublished",
                table: "SeparationRecords");
        }
    }
}
