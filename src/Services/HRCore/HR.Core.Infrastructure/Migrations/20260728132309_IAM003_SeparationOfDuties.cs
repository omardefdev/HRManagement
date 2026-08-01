using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace HR.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IAM003_SeparationOfDuties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovalDelegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegateId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityTypeScope = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RoleScope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ActiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActiveUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    RevokedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalDelegations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConflictingRolePairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleA = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RoleB = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConflictingRolePairs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SodRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MakerRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CheckerRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PosterRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ReverserRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RequireDifferentActors = table.Column<bool>(type: "boolean", nullable: false),
                    FinancialThreshold = table.Column<decimal>(type: "numeric", nullable: true),
                    RequiredApprovalCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SodRules", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ConflictingRolePairs",
                columns: new[] { "Id", "CreatedAt", "Reason", "RoleA", "RoleB" },
                values: new object[,]
                {
                    { new Guid("b2000002-0002-4000-8000-000000000001"), new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(2782), "لا يجوز الجمع بين إنشاء تشغيل الرواتب واعتماده", "payroll-processor", "payroll-approver" },
                    { new Guid("b2000002-0002-4000-8000-000000000002"), new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(3517), "لا يجوز الجمع بين اعتماد التشغيل وترحيله", "payroll-approver", "payroll-poster" },
                    { new Guid("b2000002-0002-4000-8000-000000000003"), new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(3520), "لا يجوز الجمع بين إنشاء التشغيل وترحيله", "payroll-processor", "payroll-poster" },
                    { new Guid("b2000002-0002-4000-8000-000000000004"), new DateTime(2026, 7, 28, 13, 23, 9, 34, DateTimeKind.Utc).AddTicks(3526), "كاتب بيانات الموارد البشرية لا يعتمد قرارات الإدارة العليا لنفس الكيان", "hr-core", "hr-management-approver" }
                });

            migrationBuilder.InsertData(
                table: "SodRules",
                columns: new[] { "Id", "CheckerRole", "CreatedAt", "EntityType", "FinancialThreshold", "MakerRole", "PosterRole", "RequireDifferentActors", "RequiredApprovalCount", "ReverserRole" },
                values: new object[,]
                {
                    { new Guid("a1000001-0001-4000-8000-000000000001"), "payroll-approver", new DateTime(2026, 7, 28, 13, 23, 9, 32, DateTimeKind.Utc).AddTicks(8773), "PayrollRun", 50000000m, "payroll-processor", "payroll-poster", true, 1, "payroll-poster" },
                    { new Guid("a1000001-0001-4000-8000-000000000002"), "hr-contracts", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1705), "LeaveRequest", null, "hr-contracts", null, true, 1, null },
                    { new Guid("a1000001-0001-4000-8000-000000000003"), "hr-contracts", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1722), "AdvanceRequest", 5000000m, "hr-contracts", null, true, 1, null },
                    { new Guid("a1000001-0001-4000-8000-000000000004"), "hr-management-approver", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1725), "TransferRequest", null, "hr-movements", null, true, 1, null },
                    { new Guid("a1000001-0001-4000-8000-000000000005"), "hr-management-approver", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1727), "ApprovalRequest", null, "hr-core", null, true, 1, null },
                    { new Guid("a1000001-0001-4000-8000-000000000006"), "hr-admin", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1729), "RoleAssignment", null, "auth-directory", null, true, 2, null },
                    { new Guid("a1000001-0001-4000-8000-000000000007"), "hr-management-approver", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1730), "LegalDataChange", null, "hr-core", null, true, 2, null },
                    { new Guid("a1000001-0001-4000-8000-000000000008"), "payroll-poster", new DateTime(2026, 7, 28, 13, 23, 9, 33, DateTimeKind.Utc).AddTicks(1732), "EosSettlement", 50000000m, "payroll-approver", null, true, 1, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_IsRevoked_ActiveUntil",
                table: "ApprovalDelegations",
                columns: new[] { "IsRevoked", "ActiveUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_PrincipalId_DelegateId_ActiveFrom",
                table: "ApprovalDelegations",
                columns: new[] { "PrincipalId", "DelegateId", "ActiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ConflictingRolePairs_RoleA_RoleB",
                table: "ConflictingRolePairs",
                columns: new[] { "RoleA", "RoleB" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SodRules_EntityType",
                table: "SodRules",
                column: "EntityType",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalDelegations");

            migrationBuilder.DropTable(
                name: "ConflictingRolePairs");

            migrationBuilder.DropTable(
                name: "SodRules");
        }
    }
}
