using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Compliance.Infrastructure.Data;
using HRManagement.ServiceDefaults;

#nullable disable

namespace Compliance.Infrastructure.Migrations;

[DbContext(typeof(ComplianceDbContext))]
[Migration("20260731185000_TRN002_UnionEngagement")]
public partial class TRN002_UnionEngagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "UnionEngagements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                WaveId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                EngagementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Participants = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Topics = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Objections = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ResolutionStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ScopeImpact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ExemptionAuthorityRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_UnionEngagements", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_UnionEngagements_TenantId_WaveId", table: "UnionEngagements", columns: new[] { "TenantId", "WaveId" });

        migrationBuilder.CreateTable(
            name: "CollectiveGrievances",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                FiledBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Deadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Impact = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                RoutedToIndividualPath = table.Column<bool>(type: "boolean", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_CollectiveGrievances", x => x.Id));

        migrationBuilder.CreateIndex(name: "IX_CollectiveGrievances_TenantId_Status", table: "CollectiveGrievances", columns: new[] { "TenantId", "Status" });

        TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CollectiveGrievances");
        migrationBuilder.DropTable(name: "UnionEngagements");
    }
}
