using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Movements.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TEN001_AddOrgUnitSnapshotTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "OrgUnitSnapshots",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OrgUnitSnapshots");
        }
    }
}
