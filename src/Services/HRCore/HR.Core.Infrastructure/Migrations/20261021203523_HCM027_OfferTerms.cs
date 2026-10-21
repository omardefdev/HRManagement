using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HCM027_OfferTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GradeDeviationReason",
                table: "OfferLetters",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradeMaxSalary",
                table: "OfferLetters",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradeMinSalary",
                table: "OfferLetters",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SalaryCheckedAt",
                table: "OfferLetters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalaryJustification",
                table: "OfferLetters",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SalaryUnverified",
                table: "OfferLetters",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GradeDeviationReason",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "GradeMaxSalary",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "GradeMinSalary",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "SalaryCheckedAt",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "SalaryJustification",
                table: "OfferLetters");

            migrationBuilder.DropColumn(
                name: "SalaryUnverified",
                table: "OfferLetters");
        }
    }
}
