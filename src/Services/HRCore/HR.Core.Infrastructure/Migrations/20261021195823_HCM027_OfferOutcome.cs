using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM027_OfferOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeclineReason",
                table: "OfferLetters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeclinedAt",
                table: "OfferLetters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeclinedBy",
                table: "OfferLetters",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiredAt",
                table: "OfferLetters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMeritOverride",
                table: "OfferLetters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MeritOverrideReason",
                table: "OfferLetters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokeReason",
                table: "OfferLetters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAt",
                table: "OfferLetters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ValidityDays",
                table: "OfferLetters",
                type: "integer",
                nullable: false,
                defaultValue: 14); // HCM-027: Recruitment:OfferValidityDays default; new columns only.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclineReason",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "DeclinedAt",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "DeclinedBy",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "ExpiredAt",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "IsMeritOverride",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "MeritOverrideReason",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "RevokeReason",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "RevokedAt",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "ValidityDays",
                table: "OfferLetters");
        }
    }
}
