using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Health.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN004_HealthConsentInsurance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "MedicalRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "MedicalRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentExpiresAt",
                table: "MedicalRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentGivenAt",
                table: "MedicalRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConsentPurpose",
                table: "MedicalRecords",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentWithdrawnAt",
                table: "MedicalRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "MedicalRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "InsuranceCards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "InsuranceCards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "InsuranceCards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "InsuranceCards",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "ConsentExpiresAt",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "ConsentGivenAt",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "ConsentPurpose",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "ConsentWithdrawnAt",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "MedicalRecords");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "InsuranceCards");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "InsuranceCards");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "InsuranceCards");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "InsuranceCards");
        }
    }
}
