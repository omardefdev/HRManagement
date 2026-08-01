using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IDCard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSealImageUrlColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SealImageUrl",
                table: "OfficialSealRecords");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SealImageUrl",
                table: "OfficialSealRecords",
                type: "text",
                nullable: true);
        }
    }
}
