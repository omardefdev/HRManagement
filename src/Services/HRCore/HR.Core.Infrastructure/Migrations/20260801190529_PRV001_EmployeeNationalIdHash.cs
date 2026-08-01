using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PRV001_EmployeeNationalIdHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId_NationalId",
                table: "Employees");

            migrationBuilder.AddColumn<string>(
                name: "NationalIdHash",
                table: "Employees",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_NationalIdHash",
                table: "Employees",
                columns: new[] { "TenantId", "NationalIdHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Employees_TenantId_NationalIdHash",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "NationalIdHash",
                table: "Employees");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_TenantId_NationalId",
                table: "Employees",
                columns: new[] { "TenantId", "NationalId" },
                unique: true);
        }
    }
}
