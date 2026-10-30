using System;
using File.Storage.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace File.Storage.Infrastructure.Migrations;

/// <summary>INT-002: inbox receipts for File.Storage MassTransit consumers.</summary>
/// <remarks>
/// QA-034 (owner decision Q-4): the class had no Designer partial, so without these attributes EF never discovered it
/// and no database got the IntegrationMessageReceipts table that the model snapshot (and the inbox filter) expects.
/// </remarks>
[DbContext(typeof(FileStorageDbContext))]
[Migration("20260824180000_INT002_IntegrationMessageReceipts")]
public partial class INT002_IntegrationMessageReceipts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IntegrationMessageReceipts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                MessageId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ConsumerType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationMessageReceipts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IntegrationMessageReceipts_Tenant_Message_Consumer",
            table: "IntegrationMessageReceipts",
            columns: new[] { "TenantId", "MessageId", "ConsumerType" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IntegrationMessageReceipts");
    }
}
