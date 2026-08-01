using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN002_SeparationJourneyGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "SeparationRecords",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "SeparationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "SeparationRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CompletedByName",
                table: "SeparationChecklists",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerRole",
                table: "SeparationChecklists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaHours",
                table: "SeparationChecklists",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "CompletedByName",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "OwnerRole",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "SlaHours",
                table: "SeparationChecklists");
        }
    }
}
