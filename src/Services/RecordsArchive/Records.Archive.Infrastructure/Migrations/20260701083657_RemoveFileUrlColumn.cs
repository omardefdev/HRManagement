using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Records.Archive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveFileUrlColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "DigitalFileRecords");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "DigitalFileRecords",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
