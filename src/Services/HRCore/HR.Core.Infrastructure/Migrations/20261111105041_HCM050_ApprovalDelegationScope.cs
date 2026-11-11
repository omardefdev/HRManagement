using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-50 (§5, D-50-4): one delegation entity. <c>ApprovalDelegations</c> gets the delegate employee, the names, the principal's stored
    /// scope and the revocation reason; <c>DelegateId</c> becomes optional (rows older than PLAN-50 keep it and are matched with <c>sub</c>);
    /// every row in force with no role scope, or with a role that is not delegable, is revoked ("PLAN-50 migration"); two checks keep a row in
    /// force without a role, or a row without a delegate, from existing; the unused <c>WorkflowDelegations</c> is dropped.
    /// <para>
    /// The revocation is a data change on a tenant table under forced row security, and the migrator carries no tenant: without the first
    /// step it would match no row on a real server and report success (PLAN-43's lesson). As in <c>WF044_WorkflowInstanceIntegrity</c>, the
    /// migration lifts FORCE on the one table it changes, inside its own transaction (ALTER TABLE holds an exclusive lock until the commit
    /// and rolls back with the migration), and restores it; the owner is exempt while FORCE is off. A role that does not own the table fails
    /// here, and nothing changes.
    /// </para>
    /// </summary>
    public partial class HCM050_ApprovalDelegationScope : Migration
    {
        /// <summary>The value of <c>Delegation:DelegableRoles</c> when PLAN-50 shipped (D-50-1).</summary>
        internal const string DelegableRole = "hr-management-approver";

        internal const string RevokedReason = "PLAN-50 migration";

        /// <summary>D-50-4: the rows in force with no role scope, or with a role that is not delegable.</summary>
        internal const string RevokeOldRows = $"""
            UPDATE "ApprovalDelegations"
            SET "IsRevoked" = TRUE,
                "RevokedReason" = '{RevokedReason}',
                "RevokedAt" = COALESCE("RevokedAt", now()),
                "UpdatedAt" = now()
            WHERE NOT "IsRevoked"
              AND ("RoleScope" IS NULL
                   OR btrim("RoleScope") = ''
                   OR lower(btrim("RoleScope")) <> '{DelegableRole}');
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "ApprovalDelegations" NO FORCE ROW LEVEL SECURITY;""");

            migrationBuilder.DropTable(
                name: "WorkflowDelegations");

            migrationBuilder.AlterColumn<Guid>(
                name: "DelegateId",
                table: "ApprovalDelegations",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "DelegateEmployeeId",
                table: "ApprovalDelegations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DelegateName",
                table: "ApprovalDelegations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrincipalEmployeeId",
                table: "ApprovalDelegations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrincipalName",
                table: "ApprovalDelegations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PrincipalScopeOrgUnitId",
                table: "ApprovalDelegations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevokedReason",
                table: "ApprovalDelegations",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalDelegations_DelegateEmployeeId_IsRevoked_ActiveUntil",
                table: "ApprovalDelegations",
                columns: new[] { "DelegateEmployeeId", "IsRevoked", "ActiveUntil" });

            migrationBuilder.Sql(RevokeOldRows);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalDelegations_ActiveHasRole",
                table: "ApprovalDelegations",
                sql: "\"IsRevoked\" OR (\"RoleScope\" IS NOT NULL AND trim(\"RoleScope\") <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ApprovalDelegations_HasDelegate",
                table: "ApprovalDelegations",
                sql: "\"DelegateEmployeeId\" IS NOT NULL OR \"DelegateId\" IS NOT NULL");

            migrationBuilder.Sql("""ALTER TABLE "ApprovalDelegations" FORCE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DelegateId becomes required again: a row of PLAN-50 that names an employee only cannot go back, so it is removed, which needs the
            // table unforced as in Up. The revoked rows stay revoked.
            migrationBuilder.Sql("""ALTER TABLE "ApprovalDelegations" NO FORCE ROW LEVEL SECURITY;""");
            migrationBuilder.Sql("""DELETE FROM "ApprovalDelegations" WHERE "DelegateId" IS NULL;""");

            migrationBuilder.DropIndex(
                name: "IX_ApprovalDelegations_DelegateEmployeeId_IsRevoked_ActiveUntil",
                table: "ApprovalDelegations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalDelegations_ActiveHasRole",
                table: "ApprovalDelegations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ApprovalDelegations_HasDelegate",
                table: "ApprovalDelegations");

            migrationBuilder.DropColumn(
                name: "DelegateEmployeeId",
                table: "ApprovalDelegations");

            migrationBuilder.DropColumn(
                name: "DelegateName",
                table: "ApprovalDelegations");

            migrationBuilder.DropColumn(
                name: "PrincipalEmployeeId",
                table: "ApprovalDelegations");

            migrationBuilder.DropColumn(
                name: "PrincipalName",
                table: "ApprovalDelegations");

            migrationBuilder.DropColumn(
                name: "PrincipalScopeOrgUnitId",
                table: "ApprovalDelegations");

            migrationBuilder.DropColumn(
                name: "RevokedReason",
                table: "ApprovalDelegations");

            migrationBuilder.AlterColumn<Guid>(
                name: "DelegateId",
                table: "ApprovalDelegations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.Sql("""ALTER TABLE "ApprovalDelegations" FORCE ROW LEVEL SECURITY;""");

            migrationBuilder.CreateTable(
                name: "WorkflowDelegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActiveUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DelegateId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DomainEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    PrincipalId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrincipalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WorkflowCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDelegations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDelegations_IsRevoked_ActiveUntil",
                table: "WorkflowDelegations",
                columns: new[] { "IsRevoked", "ActiveUntil" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDelegations_PrincipalId_DelegateId_ActiveFrom",
                table: "WorkflowDelegations",
                columns: new[] { "PrincipalId", "DelegateId", "ActiveFrom" });
        }
    }
}
