using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace File.Storage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA001_SyncModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsRevoked",
                table: "FileAccessGrants",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsRevoked",
                table: "FileAccessGrants",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }
    }
}
