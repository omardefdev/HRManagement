using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(GatewayAuditDbContext))]
[Migration("20260824120000_PLAN01_GatewayAuditScopeFields")]
public partial class PLAN01_GatewayAuditScopeFields : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "gateway_audit_entries",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ScopeOrgUnitId",
            table: "gateway_audit_entries",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Reason",
            table: "gateway_audit_entries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResourceType",
            table: "gateway_audit_entries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ResourceId",
            table: "gateway_audit_entries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_gateway_audit_entries_TenantId",
            table: "gateway_audit_entries",
            column: "TenantId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_gateway_audit_entries_TenantId",
            table: "gateway_audit_entries");

        migrationBuilder.DropColumn(name: "TenantId", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "ScopeOrgUnitId", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "Reason", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "ResourceType", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "ResourceId", table: "gateway_audit_entries");
    }
}
