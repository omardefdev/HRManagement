using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Records.Archive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDigitalFileRecordFileId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FileId",
                table: "DigitalFileRecords",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileId",
                table: "DigitalFileRecords");
        }
    }
}
