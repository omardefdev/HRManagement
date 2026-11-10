using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class POL049_PolicyDomainRetirement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAt",
                table: "PolicyTemplates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetiredReason",
                table: "PolicyTemplates",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "PolicyTemplates");

            migrationBuilder.DropColumn(
                name: "RetiredReason",
                table: "PolicyTemplates");
        }
    }
}
