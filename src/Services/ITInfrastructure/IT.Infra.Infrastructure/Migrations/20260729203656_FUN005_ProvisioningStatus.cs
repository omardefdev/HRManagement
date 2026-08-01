using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IT.Infra.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FUN005_ProvisioningStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ProvisionedAt",
                table: "ITSupportTickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningError",
                table: "ITSupportTickets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProvisioningStatus",
                table: "ITSupportTickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProvisionedAt",
                table: "ITSupportTickets");

            migrationBuilder.DropColumn(
                name: "ProvisioningError",
                table: "ITSupportTickets");

            migrationBuilder.DropColumn(
                name: "ProvisioningStatus",
                table: "ITSupportTickets");
        }
    }
}
