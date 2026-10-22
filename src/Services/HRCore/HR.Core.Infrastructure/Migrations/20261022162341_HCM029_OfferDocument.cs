using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <summary>
    /// HCM-029 (D2): offer letter hash stored at issue. New nullable columns only — no data step; offers issued before
    /// this migration keep null and their Details page says no hash was recorded.
    /// </summary>
    public partial class HCM029_OfferDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DocumentGeneratedAt",
                table: "OfferLetters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentSha256",
                table: "OfferLetters",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DocumentGeneratedAt",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "DocumentSha256",
                table: "OfferLetters");
        }
    }
}
