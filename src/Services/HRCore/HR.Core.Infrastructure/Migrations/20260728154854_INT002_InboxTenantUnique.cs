using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class INT002_InboxTenantUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationMessageReceipts_MessageId_ConsumerType",
                table: "IntegrationMessageReceipts");

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "IntegrationMessageReceipts",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessedAt",
                table: "IntegrationMessageReceipts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 697, DateTimeKind.Utc).AddTicks(1394));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 697, DateTimeKind.Utc).AddTicks(2078));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 697, DateTimeKind.Utc).AddTicks(2080));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 697, DateTimeKind.Utc).AddTicks(2082));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(305));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1975));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1982));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1985));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000005"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1986));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000006"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1988));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000007"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1989));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000008"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 15, 48, 53, 696, DateTimeKind.Utc).AddTicks(1991));

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMessageReceipts_Tenant_Message_Consumer",
                table: "IntegrationMessageReceipts",
                columns: new[] { "TenantId", "MessageId", "ConsumerType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntegrationMessageReceipts_Tenant_Message_Consumer",
                table: "IntegrationMessageReceipts");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "IntegrationMessageReceipts");

            migrationBuilder.DropColumn(
                name: "ProcessedAt",
                table: "IntegrationMessageReceipts");

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(7479));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(8361));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(8365));

            migrationBuilder.UpdateData(
                table: "ConflictingRolePairs",
                keyColumn: "Id",
                keyValue: new Guid("b2000002-0002-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 452, DateTimeKind.Utc).AddTicks(8367));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000001"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(5183));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000002"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7314));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000003"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7323));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000004"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7326));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000005"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7328));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000006"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7330));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000007"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7331));

            migrationBuilder.UpdateData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000008"),
                column: "CreatedAt",
                value: new DateTime(2026, 7, 28, 14, 37, 19, 451, DateTimeKind.Utc).AddTicks(7333));

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMessageReceipts_MessageId_ConsumerType",
                table: "IntegrationMessageReceipts",
                columns: new[] { "MessageId", "ConsumerType" },
                unique: true);
        }
    }
}
