using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-52 (D-52-2, P-6): the scope keys HR.Core lacked. <c>DiwanCorrespondences</c> and <c>FieldInspectionVisits</c> get a nullable
    /// <c>OrgUnitId</c> with an index; Diwan's old rows take their related employee's unit, the others stay empty (central, D-52-1); old
    /// inspections stay empty (<c>LeadInspectorId</c> is the inspector, not the subject). <c>WorkflowInstances.ScopeOrgUnitId</c> (PLAN-43),
    /// where it is null, is filled per entity type with the logic of <c>WorkflowInstanceScopeResolver</c>; a type with no key, and an external
    /// type whose request carried no unit, stay null (central).
    /// <para>
    /// The backfills are data changes on tenant tables under forced row security, and the migrator carries no tenant: without the first step
    /// they would match no row on a real server and report success (PLAN-43's lesson). As in <c>WF044_WorkflowInstanceIntegrity</c> and
    /// <c>HCM050_ApprovalDelegationScope</c>, the migration lifts FORCE on every table it reads or writes, inside its own transaction (ALTER
    /// TABLE holds an exclusive lock until the commit and rolls back with the migration), and restores it on exactly the tables that had it.
    /// A role that does not own them fails here, and nothing changes.
    /// </para>
    /// </summary>
    public partial class HCM052_OrgScopeKeys : Migration
    {
        /// <summary>Every table the backfills read or write.</summary>
        public static readonly string[] TablesTouched =
        [
            "DiwanCorrespondences",
            "Employees",
            "WorkflowInstances",
            "RecruitmentRequisitions",
            "FundedPositions",
            "OfferLetters",
            "SelectionCommitteeSessions",
            "RecruitmentAppeals",
            "RecruitmentApplications"
        ];

        /// <summary>Records which of the tables are forced now, then lifts FORCE on them for this transaction.</summary>
        internal static string LiftForce =>
            "CREATE TEMP TABLE hcm052_forced ON COMMIT DROP AS\n"
            + "    SELECT c.relname::text AS name\n"
            + "    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace\n"
            + "    WHERE n.nspname = current_schema() AND c.relkind = 'r' AND c.relforcerowsecurity\n"
            + "      AND c.relname IN (" + string.Join(", ", Array.ConvertAll(TablesTouched, t => "'" + t + "'")) + ");\n"
            + ForEachRecorded("NO FORCE");

        /// <summary>Restores FORCE on exactly the tables that had it.</summary>
        internal static string RestoreForce => ForEachRecorded("FORCE") + "DROP TABLE hcm052_forced;\n";

        private static string ForEachRecorded(string clause) =>
            "DO $$\n"
            + "DECLARE t text;\n"
            + "BEGIN\n"
            + "    FOR t IN SELECT name FROM hcm052_forced LOOP\n"
            + "        EXECUTE format('ALTER TABLE %I " + clause + " ROW LEVEL SECURITY', t);\n"
            + "    END LOOP;\n"
            + "END $$;\n";

        /// <summary>D-52-2: a Diwan record takes its related employee's unit; without one it stays central.</summary>
        internal const string BackfillDiwan = """
            UPDATE "DiwanCorrespondences" d
            SET "OrgUnitId" = e."PrimaryOrgUnitId"
            FROM "Employees" e
            WHERE d."OrgUnitId" IS NULL
              AND d."RelatedEmployeeId" = e."Id"
              AND e."TenantId" = d."TenantId"
              AND e."PrimaryOrgUnitId" IS NOT NULL;
            """;

        /// <summary>The resolver's logic per type: the funded position's unit, directly or through the requisition and the application.</summary>
        internal const string BackfillWorkflowInstances = """
            UPDATE "WorkflowInstances" w
            SET "ScopeOrgUnitId" = p."OrgUnitId"
            FROM "RecruitmentRequisitions" r
            JOIN "FundedPositions" p ON p."Id" = r."FundedPositionId" AND p."TenantId" = r."TenantId"
            WHERE w."ScopeOrgUnitId" IS NULL AND w."EntityType" = 'RecruitmentRequisition'
              AND w."EntityId" = r."Id" AND r."TenantId" = w."TenantId";

            UPDATE "WorkflowInstances" w
            SET "ScopeOrgUnitId" = p."OrgUnitId"
            FROM "OfferLetters" o
            JOIN "FundedPositions" p ON p."Id" = o."FundedPositionId" AND p."TenantId" = o."TenantId"
            WHERE w."ScopeOrgUnitId" IS NULL AND w."EntityType" = 'OfferLetter'
              AND w."EntityId" = o."Id" AND o."TenantId" = w."TenantId";

            UPDATE "WorkflowInstances" w
            SET "ScopeOrgUnitId" = p."OrgUnitId"
            FROM "SelectionCommitteeSessions" s
            JOIN "RecruitmentRequisitions" r ON r."Id" = s."RequisitionId" AND r."TenantId" = s."TenantId"
            JOIN "FundedPositions" p ON p."Id" = r."FundedPositionId" AND p."TenantId" = r."TenantId"
            WHERE w."ScopeOrgUnitId" IS NULL AND w."EntityType" = 'SelectionCommitteeSession'
              AND w."EntityId" = s."Id" AND s."TenantId" = w."TenantId";

            UPDATE "WorkflowInstances" w
            SET "ScopeOrgUnitId" = p."OrgUnitId"
            FROM "RecruitmentAppeals" a
            JOIN "RecruitmentApplications" app ON app."Id" = a."RecruitmentApplicationId" AND app."TenantId" = a."TenantId"
            JOIN "RecruitmentRequisitions" r ON r."Id" = app."RequisitionId" AND r."TenantId" = app."TenantId"
            JOIN "FundedPositions" p ON p."Id" = r."FundedPositionId" AND p."TenantId" = r."TenantId"
            WHERE w."ScopeOrgUnitId" IS NULL AND w."EntityType" = 'RecruitmentAppeal'
              AND w."EntityId" = a."Id" AND a."TenantId" = w."TenantId";
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(LiftForce);

            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                table: "FieldInspectionVisits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OrgUnitId",
                table: "DiwanCorrespondences",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FieldInspectionVisits_OrgUnitId",
                table: "FieldInspectionVisits",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DiwanCorrespondences_OrgUnitId",
                table: "DiwanCorrespondences",
                column: "OrgUnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiwanCorrespondences_OrgUnits_OrgUnitId",
                table: "DiwanCorrespondences",
                column: "OrgUnitId",
                principalTable: "OrgUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_FieldInspectionVisits_OrgUnits_OrgUnitId",
                table: "FieldInspectionVisits",
                column: "OrgUnitId",
                principalTable: "OrgUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(BackfillDiwan);
            migrationBuilder.Sql(BackfillWorkflowInstances);

            migrationBuilder.Sql(RestoreForce);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfilled ScopeOrgUnitId values stay: PLAN-43's column existed before, and each value is what the resolver computes.
            migrationBuilder.DropForeignKey(
                name: "FK_DiwanCorrespondences_OrgUnits_OrgUnitId",
                table: "DiwanCorrespondences");

            migrationBuilder.DropForeignKey(
                name: "FK_FieldInspectionVisits_OrgUnits_OrgUnitId",
                table: "FieldInspectionVisits");

            migrationBuilder.DropIndex(
                name: "IX_FieldInspectionVisits_OrgUnitId",
                table: "FieldInspectionVisits");

            migrationBuilder.DropIndex(
                name: "IX_DiwanCorrespondences_OrgUnitId",
                table: "DiwanCorrespondences");

            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                table: "FieldInspectionVisits");

            migrationBuilder.DropColumn(
                name: "OrgUnitId",
                table: "DiwanCorrespondences");
        }
    }
}
