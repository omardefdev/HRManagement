using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IDCard.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSealFileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SealFileId",
                table: "OfficialSealRecords",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SealFileId",
                table: "OfficialSealRecords");
        }
    }
}
