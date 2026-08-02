using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payroll.Infrastructure.Migrations
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

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationMessageReceipts_MessageId_ConsumerType",
                table: "IntegrationMessageReceipts",
                columns: new[] { "MessageId", "ConsumerType" },
                unique: true);
        }
    }
}
