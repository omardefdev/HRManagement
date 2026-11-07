using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Core.Infrastructure.Migrations
{
    /// <summary>
    /// PLAN-44 (G-WF-12): at most one running instance per entity and per idempotency key, after resolving existing duplicates.
    /// <para>
    /// Every table read here is under forced row security and the migrator carries no tenant, so without the first step the statements
    /// would see no row at all and report success (PLAN-43's lesson). The owner is exempt once FORCE is off, so the migration lifts it on
    /// the eight tables it reads, inside its own transaction. ALTER TABLE holds an exclusive lock until the commit and rolls back with the
    /// migration, so no other session ever sees a table unforced. A role that does not own them fails here, and nothing changes.
    /// </para>
    /// </summary>
    public partial class WF044_WorkflowInstanceIntegrity : Migration
    {
        /// <summary>The instances, and each entity table that links one.</summary>
        internal static readonly string[] TablesRead =
        [
            "WorkflowInstances",
            "RecruitmentRequisitions",
            "OfferLetters",
            "EstablishmentChangeRequests",
            "SelectionCommitteeSessions",
            "RecruitmentAppeals",
            "RecruitmentCompetitions",
            "CompetitionResultVersions"
        ];

        /// <summary>
        /// Every running instance with its group. External types have no link here, so their groups have no linked instance.
        /// <c>keep</c> is 1 for the instance that stays: the linked one, otherwise the newest.
        /// </summary>
        private const string RankedInstances =
            """
            WITH links AS (
                SELECT 'RecruitmentRequisition' AS entity_type, "Id" AS entity_id, "WorkflowInstanceId" AS instance_id FROM "RecruitmentRequisitions"
                UNION ALL SELECT 'OfferLetter', "Id", "WorkflowInstanceId" FROM "OfferLetters"
                UNION ALL SELECT 'EstablishmentChangeRequest', "Id", "WorkflowInstanceId" FROM "EstablishmentChangeRequests"
                UNION ALL SELECT 'SelectionCommitteeSession', "Id", "WorkflowInstanceId" FROM "SelectionCommitteeSessions"
                UNION ALL SELECT 'RecruitmentAppeal', "Id", "WorkflowInstanceId" FROM "RecruitmentAppeals"
                UNION ALL SELECT 'RecruitmentCompetition', "Id", "ApprovalWorkflowInstanceId" FROM "RecruitmentCompetitions"
                UNION ALL SELECT 'CompetitionResultVersion', "Id", "WorkflowInstanceId" FROM "CompetitionResultVersions"
            ),
            ranked AS (
                SELECT i."Id",
                       i."TenantId",
                       i."EntityType",
                       i."EntityId",
                       i."CurrentStepLevel",
                       jsonb_array_length(COALESCE(NULLIF(btrim(i."StepHistoryJson"), ''), '[]')::jsonb) > 0 AS decided,
                       count(*) OVER grp AS copies,
                       bool_or(l.instance_id IS NOT NULL) OVER grp AS group_linked,
                       row_number() OVER (
                           PARTITION BY i."TenantId", i."EntityType", i."EntityId"
                           ORDER BY (l.instance_id IS NOT NULL) DESC, i."StartedAt" DESC, i."Id" DESC) AS keep
                FROM "WorkflowInstances" i
                LEFT JOIN links l
                    ON l.entity_type = i."EntityType" AND l.entity_id = i."EntityId" AND l.instance_id = i."Id"
                WHERE i."Status" = 'InProgress'
                WINDOW grp AS (PARTITION BY i."TenantId", i."EntityType", i."EntityId")
            )
            """;

        /// <summary>The plan refuses to choose between decisions when nothing links one of them: the operator decides.</summary>
        private const string RefuseUndecidableGroups =
            $$"""
            DO $wf044$
            DECLARE
                undecidable text;
            BEGIN
                {{RankedInstances}}
                SELECT string_agg(format('%s %s %s', "TenantId", "EntityType", "EntityId"), '; ')
                INTO undecidable
                FROM ranked
                WHERE copies > 1 AND NOT group_linked AND decided;

                IF undecidable IS NOT NULL THEN
                    RAISE EXCEPTION 'PLAN-44: duplicate running instances with a decision and no link, the operator must choose: %', undecidable;
                END IF;
            END
            $wf044$;
            """;

        private const string RefuseDuplicateKeys =
            """
            DO $wf044$
            DECLARE
                duplicated text;
            BEGIN
                SELECT string_agg(format('%s %s %s', "TenantId", "SourceClientId", "IdempotencyKey"), '; ')
                INTO duplicated
                FROM (
                    SELECT "TenantId", "SourceClientId", "IdempotencyKey"
                    FROM "WorkflowInstances"
                    WHERE "IdempotencyKey" IS NOT NULL
                    GROUP BY "TenantId", "SourceClientId", "IdempotencyKey"
                    HAVING count(*) > 1
                ) d;

                IF duplicated IS NOT NULL THEN
                    RAISE EXCEPTION 'PLAN-44: one idempotency key on two instances: %', duplicated;
                END IF;
            END
            $wf044$;
            """;

        /// <summary>The entry has the shape of WorkflowStepHistoryEntry; 4 is WorkflowStepStatus.Withdrawn.</summary>
        private const string CancelDuplicates =
            $$"""
            {{RankedInstances}}
            UPDATE "WorkflowInstances" w
            SET "Status" = 'Cancelled',
                "CompletedAt" = now(),
                "StepHistoryJson" = (
                    COALESCE(NULLIF(btrim(w."StepHistoryJson"), ''), '[]')::jsonb
                    || jsonb_build_array(jsonb_build_object(
                        'Level', r."CurrentStepLevel",
                        'StepName', 'PLAN-44: نسخة مكررة',
                        'Status', 4,
                        'ActorId', NULL,
                        'ActorName', NULL,
                        'Comments', 'PLAN-44: نسخة مكررة',
                        'ActionAt', to_char(now() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS"Z"')))
                )::text
            FROM ranked r
            WHERE r."Id" = w."Id" AND r.copies > 1 AND r.keep > 1;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TablesRead)
                migrationBuilder.Sql($"""ALTER TABLE "{table}" NO FORCE ROW LEVEL SECURITY;""");

            migrationBuilder.Sql(RefuseUndecidableGroups);
            migrationBuilder.Sql(RefuseDuplicateKeys);
            migrationBuilder.Sql(CancelDuplicates);

            // xmin is a system column: Npgsql emits nothing for it, and the previous tag runs beside this schema unchanged.
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "WorkflowInstances",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_Tenant_Entity_Active",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "EntityType", "EntityId" },
                unique: true,
                filter: "\"Status\" = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowInstances_Tenant_Source_IdempotencyKey",
                table: "WorkflowInstances",
                columns: new[] { "TenantId", "SourceClientId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");

            foreach (var table in TablesRead)
                migrationBuilder.Sql($"""ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_Tenant_Entity_Active",
                table: "WorkflowInstances");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowInstances_Tenant_Source_IdempotencyKey",
                table: "WorkflowInstances");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "WorkflowInstances");
        }
    }
}
