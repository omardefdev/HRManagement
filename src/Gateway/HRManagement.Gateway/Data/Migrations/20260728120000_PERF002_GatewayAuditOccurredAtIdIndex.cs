using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations
{
    [DbContext(typeof(GatewayAuditDbContext))]
    [Migration("20260728120000_PERF002_GatewayAuditOccurredAtIdIndex")]
    public partial class PERF002_GatewayAuditOccurredAtIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_gateway_audit_entries_OccurredAt_Id",
                table: "gateway_audit_entries",
                columns: new[] { "OccurredAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_gateway_audit_entries_OccurredAt_Id",
                table: "gateway_audit_entries");
        }
    }
}
