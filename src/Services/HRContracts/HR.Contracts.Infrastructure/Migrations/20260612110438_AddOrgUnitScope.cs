using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgUnitScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                table: "LeaveRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                table: "AdvanceRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrgUnitSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterializedPath = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnitSnapshots", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrgUnitSnapshots");

            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                table: "AdvanceRequests");
        }
    }
}
