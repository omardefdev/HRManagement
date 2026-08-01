using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPayProfileOrgUnitId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                table: "EmployeePayProfiles",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                table: "EmployeePayProfiles");
        }
    }
}
