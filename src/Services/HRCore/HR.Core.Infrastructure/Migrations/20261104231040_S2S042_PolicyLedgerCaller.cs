using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S2S042_PolicyLedgerCaller : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CallerClientId",
                table: "PolicyEnforcementLedgers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EntityId",
                table: "PolicyEnforcementLedgers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubjectType",
                table: "PolicyEnforcementLedgers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PolicyEnforcementLedgers_EntityId_OccurredAt",
                table: "PolicyEnforcementLedgers",
                columns: new[] { "EntityId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PolicyEnforcementLedgers_EntityId_OccurredAt",
                table: "PolicyEnforcementLedgers");

            migrationBuilder.DropColumn(
                name: "CallerClientId",
                table: "PolicyEnforcementLedgers");

            migrationBuilder.DropColumn(
                name: "EntityId",
                table: "PolicyEnforcementLedgers");

            migrationBuilder.DropColumn(
                name: "SubjectType",
                table: "PolicyEnforcementLedgers");
        }
    }
}
