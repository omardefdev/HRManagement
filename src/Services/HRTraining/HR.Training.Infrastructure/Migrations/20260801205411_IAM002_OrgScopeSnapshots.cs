using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Training.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IAM002_OrgScopeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeOrgUnitSnapshots",
                columns: table => new
                {
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryOrgUnitId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeOrgUnitSnapshots", x => x.EmployeeId);
                });

            migrationBuilder.CreateTable(
                name: "OrgUnitSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterializedPath = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnitSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeOrgUnitSnapshots_PrimaryOrgUnitId",
                table: "EmployeeOrgUnitSnapshots",
                column: "PrimaryOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnitSnapshots_ParentId",
                table: "OrgUnitSnapshots",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnitSnapshots_TenantId_MaterializedPath",
                table: "OrgUnitSnapshots",
                columns: new[] { "TenantId", "MaterializedPath" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeOrgUnitSnapshots");

            migrationBuilder.DropTable(
                name: "OrgUnitSnapshots");
        }
    }
}
