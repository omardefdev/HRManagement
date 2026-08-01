using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Tables/columns for JobCatalog, FundedPositions, OrgUnitVersions, Workflows,
    /// SignatureRecords, ReferenceCatalogs, and JobClassificationCode are created by
    /// earlier migrations (MDM001/WF001/ORG001/SIG001/EST001). This migration only
    /// inserts remaining SoD seed rows that were pending in the model snapshot.
    /// </remarks>
    public partial class DemoSeedModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "SodRules",
                columns: new[] { "Id", "CheckerRole", "CreatedAt", "EntityType", "FinancialThreshold", "MakerRole", "PosterRole", "RequireDifferentActors", "RequiredApprovalCount", "ReverserRole" },
                values: new object[,]
                {
                    { new Guid("a1000001-0001-4000-8000-000000000014"), "hr-management-approver", new DateTime(2026, 7, 28, 16, 26, 11, 643, DateTimeKind.Utc).AddTicks(1485), "SecondmentRequest", null, "hr-movements", null, true, 1, null },
                    { new Guid("a1000001-0001-4000-8000-000000000015"), "hr-management-approver", new DateTime(2026, 7, 28, 16, 26, 11, 643, DateTimeKind.Utc).AddTicks(1488), "LoanAssignmentRequest", null, "hr-movements", null, true, 1, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000014"));

            migrationBuilder.DeleteData(
                table: "SodRules",
                keyColumn: "Id",
                keyValue: new Guid("a1000001-0001-4000-8000-000000000015"));
        }
    }
}
