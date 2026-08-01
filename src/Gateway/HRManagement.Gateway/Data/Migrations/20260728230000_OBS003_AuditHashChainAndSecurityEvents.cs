using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRManagement.Gateway.Data.Migrations;

/// <inheritdoc />
[DbContext(typeof(GatewayAuditDbContext))]
[Migration("20260728230000_OBS003_AuditHashChainAndSecurityEvents")]
public partial class OBS003_AuditHashChainAndSecurityEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ChainKey",
            table: "gateway_audit_entries",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "default");

        migrationBuilder.AddColumn<string>(
            name: "PrevHash",
            table: "gateway_audit_entries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "EventHash",
            table: "gateway_audit_entries",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_gateway_audit_entries_ChainKey_SequenceNumber",
            table: "gateway_audit_entries",
            columns: new[] { "ChainKey", "SequenceNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_gateway_audit_entries_EventHash",
            table: "gateway_audit_entries",
            column: "EventHash");

        migrationBuilder.CreateTable(
            name: "security_audit_events",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ActorSub = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Resource = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                DetailJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_security_audit_events", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_security_audit_events_OccurredAtUtc",
            table: "security_audit_events",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_security_audit_events_EventType",
            table: "security_audit_events",
            column: "EventType");

        migrationBuilder.CreateTable(
            name: "audit_legal_holds",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                RangeStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RangeEndUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                UserSub = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Active = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_audit_legal_holds", x => x.Id));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "security_audit_events");
        migrationBuilder.DropTable(name: "audit_legal_holds");
        migrationBuilder.DropIndex(name: "IX_gateway_audit_entries_EventHash", table: "gateway_audit_entries");
        migrationBuilder.DropIndex(name: "IX_gateway_audit_entries_ChainKey_SequenceNumber", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "EventHash", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "PrevHash", table: "gateway_audit_entries");
        migrationBuilder.DropColumn(name: "ChainKey", table: "gateway_audit_entries");
    }
}
