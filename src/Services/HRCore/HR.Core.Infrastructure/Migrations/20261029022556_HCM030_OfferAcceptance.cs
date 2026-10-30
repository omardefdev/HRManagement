using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM030_OfferAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AcceptanceEvidenceFileId",
                table: "OfferLetters",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptanceNote",
                table: "OfferLetters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedBy",
                table: "OfferLetters",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptanceEvidenceFileId",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "AcceptanceNote",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "AcceptedBy",
                table: "OfferLetters");
        }
    }
}
