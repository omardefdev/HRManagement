using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ORG001_HeadOnVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HeadEmployeeId",
                table: "OrgUnitVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.InsertData(
                table: "SodRules",
                columns: new[] { "Id", "CheckerRole", "CreatedAt", "EntityType", "FinancialThreshold", "MakerRole", "PosterRole", "RequireDifferentActors", "RequiredApprovalCount", "ReverserRole" },
                values: new object[] { new Guid("a1000001-0001-4000-8000-000000000018"), "payroll-approver", new DateTime(2026, 7, 28, 16, 26, 11, 643, DateTimeKind.Utc).AddTicks(1497), "PaymentBatch", 50000000m, "payroll-poster", "payroll-poster", true, 1, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000018"));

            migrationBuilder.DropColumn(
                name: "HeadEmployeeId",
                table: "OrgUnitVersions");
        }
    }
}
