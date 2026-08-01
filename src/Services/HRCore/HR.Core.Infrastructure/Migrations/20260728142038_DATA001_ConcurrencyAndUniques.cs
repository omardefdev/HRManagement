using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DATA001_ConcurrencyAndUniques : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ApprovalRequests",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(1673));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(2325));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(2328));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 406, DateTimeKind.Utc).AddTicks(2335));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 404, DateTimeKind.Utc).AddTicks(9932));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1536));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1543));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1546));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000005"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1547));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000006"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1549));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000007"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1555));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000008"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 20, 37, 405, DateTimeKind.Utc).AddTicks(1557));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ApprovalRequests");

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(2782));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(3517));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(3520));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(3526));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 32, DateTimeKind.Utc).AddTicks(8773));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1705));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1722));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1725));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000005"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1727));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000006"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1729));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000007"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1730));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000008"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1732));
        }
    }
}
