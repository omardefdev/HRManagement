using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN001_HireChecklistGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompletedBy",
                table: "HireChecklists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompletedByName",
                table: "HireChecklists",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceRef",
                table: "HireChecklists",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerRole",
                table: "HireChecklists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaHours",
                table: "HireChecklists",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedBy",
                table: "HireChecklists");

            migrationBuilder.DropColumn(
                name: "CompletedByName",
                table: "HireChecklists");

            migrationBuilder.DropColumn(
                name: "EvidenceRef",
                table: "HireChecklists");

            migrationBuilder.DropColumn(
                name: "OwnerRole",
                table: "HireChecklists");

            migrationBuilder.DropColumn(
                name: "SlaHours",
                table: "HireChecklists");
        }
    }
}
