using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TEN003_TenantAssignmentAndQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantAssignmentRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetTable = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DiscoveryMethod = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SqlCondition = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TargetTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAssignmentRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantQuarantineEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TableName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantQuarantineEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantAssignmentRules_TargetTable_DiscoveryMethod_TargetTen~",
                table: "TenantAssignmentRules",
                columns: new[] { "TargetTable", "DiscoveryMethod", "TargetTenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_TenantQuarantineEntries_ServiceName_TableName_RecordId",
                table: "TenantQuarantineEntries",
                columns: new[] { "ServiceName", "TableName", "RecordId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantAssignmentRules");

            migrationBuilder.DropTable(
                name: "TenantQuarantineEntries");
        }
    }
}
