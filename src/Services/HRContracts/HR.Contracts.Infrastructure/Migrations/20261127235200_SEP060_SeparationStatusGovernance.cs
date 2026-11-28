using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR.Contracts.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SEP060_SeparationStatusGovernance : Migration
    {
        /// <summary>
        /// PLAN-60 (P-5): every record written before the state machine gets its status, deterministically, in this order. An UPDATE by
        /// the migrator matches no row under forced row security (PLAN-43), so FORCE is lifted on each table the backfill reads or writes,
        /// inside the migration's transaction, and put back exactly as it was (Compliance's WF046 is the model). A record whose saga
        /// started is never cancelled as a duplicate: two such records of one employee stop the migration with their ids.
        /// </summary>
        public const string Backfill = """
            DO $$
            DECLARE
                force_records boolean;
                force_items boolean;
                force_sagas boolean;
                force_transactions boolean;
                twice record;
                pair record;
            BEGIN
                SELECT relforcerowsecurity INTO force_records FROM pg_class WHERE oid = '"SeparationRecords"'::regclass;
                SELECT relforcerowsecurity INTO force_items FROM pg_class WHERE oid = '"SeparationChecklists"'::regclass;
                SELECT relforcerowsecurity INTO force_sagas FROM pg_class WHERE oid = '"SeparationSagaStates"'::regclass;
                SELECT relforcerowsecurity INTO force_transactions FROM pg_class WHERE oid = '"ServiceTransactions"'::regclass;
                IF force_records THEN EXECUTE 'ALTER TABLE "SeparationRecords" NO FORCE ROW LEVEL SECURITY'; END IF;
                IF force_items THEN EXECUTE 'ALTER TABLE "SeparationChecklists" NO FORCE ROW LEVEL SECURITY'; END IF;
                IF force_sagas THEN EXECUTE 'ALTER TABLE "SeparationSagaStates" NO FORCE ROW LEVEL SECURITY'; END IF;
                IF force_transactions THEN EXECUTE 'ALTER TABLE "ServiceTransactions" NO FORCE ROW LEVEL SECURITY'; END IF;

                -- Rule 1: a cancelled record is Cancelled.
                UPDATE "SeparationRecords" SET "Status" = 'Cancelled', "IsLegacyDraft" = false WHERE "IsCancelled";

                -- Rule 2: a record whose saga started (published, or with a saga row: the old compensation reset the flag), by its saga's
                -- state; no row, AwaitingParties, Compensating or Faulted is Executing. A compensated saga (Cancelled) cancels its record.
                UPDATE "SeparationRecords" r
                SET "Status" = COALESCE((
                        SELECT CASE s."CurrentState"
                                   WHEN 'Completed' THEN 'Completed'
                                   WHEN 'AwaitingIntervention' THEN 'AwaitingIntervention'
                                   WHEN 'Cancelled' THEN 'Cancelled'
                                   ELSE 'Executing' END
                        FROM "SeparationSagaStates" s
                        WHERE s."SeparationRecordId" = r."Id"
                        ORDER BY s."UpdatedAt" DESC
                        LIMIT 1), 'Executing'),
                    "IsLegacyDraft" = false
                WHERE NOT r."IsCancelled"
                  AND (r."SeparationEventPublished"
                       OR EXISTS (SELECT 1 FROM "SeparationSagaStates" s WHERE s."SeparationRecordId" = r."Id"));
                UPDATE "SeparationRecords"
                SET "IsCancelled" = true,
                    "CancelledAt" = COALESCE("CancelledAt", now()),
                    "CancellationReason" = COALESCE("CancellationReason", 'إلغاء رحلة الفصل')
                WHERE "Status" = 'Cancelled' AND NOT "IsCancelled";

                -- Rule 3: the rest (never started) is a Draft that must pass the approval, marked as an older record.
                UPDATE "SeparationRecords" r SET "Status" = 'Draft', "IsLegacyDraft" = true
                WHERE NOT r."IsCancelled" AND NOT r."SeparationEventPublished"
                  AND NOT EXISTS (SELECT 1 FROM "SeparationSagaStates" s WHERE s."SeparationRecordId" = r."Id");

                -- Rule 4: a record with no employee can never be approved.
                UPDATE "SeparationRecords"
                SET "Status" = 'Cancelled', "IsLegacyDraft" = false, "IsCancelled" = true, "CancelledAt" = now(),
                    "CancellationReason" = 'PLAN-60: سجل بلا موظف — لا يمكن اعتماده'
                WHERE "EmployeeId" = '00000000-0000-0000-0000-000000000000' AND "Status" = 'Draft';

                -- Rule 5: open duplicates per (TenantId, EmployeeId). Never cancel a record whose saga started.
                FOR twice IN
                    SELECT r."EmployeeId", string_agg(r."Id"::text, ', ' ORDER BY r."Id") AS ids
                    FROM "SeparationRecords" r
                    WHERE r."Status" IN ('Draft', 'PendingApproval', 'Approved', 'Executing', 'AwaitingIntervention')
                      AND (r."SeparationEventPublished"
                           OR EXISTS (SELECT 1 FROM "SeparationSagaStates" s WHERE s."SeparationRecordId" = r."Id"))
                    GROUP BY r."TenantId", r."EmployeeId"
                    HAVING count(*) > 1
                LOOP
                    RAISE EXCEPTION 'PLAN-60 (SEP060): employee % has two separation records whose sagas started (%); neither is cancelled, the migration stops.',
                        twice."EmployeeId", twice.ids;
                END LOOP;

                WITH ranked AS (
                    SELECT r."Id",
                           (r."SeparationEventPublished"
                            OR EXISTS (SELECT 1 FROM "SeparationSagaStates" s WHERE s."SeparationRecordId" = r."Id")) AS started,
                           row_number() OVER (
                               PARTITION BY r."TenantId", r."EmployeeId"
                               ORDER BY (r."SeparationEventPublished"
                                         OR EXISTS (SELECT 1 FROM "SeparationSagaStates" s WHERE s."SeparationRecordId" = r."Id")) DESC,
                                        r."CreatedAt" DESC, r."Id" DESC) AS rank
                    FROM "SeparationRecords" r
                    WHERE r."Status" IN ('Draft', 'PendingApproval', 'Approved', 'Executing', 'AwaitingIntervention'))
                UPDATE "SeparationRecords" t
                SET "Status" = 'Cancelled', "IsLegacyDraft" = false, "IsCancelled" = true, "CancelledAt" = now(),
                    "CancellationReason" = 'PLAN-60: سجل مكرر'
                FROM ranked
                WHERE t."Id" = ranked."Id" AND ranked.rank > 1 AND NOT ranked.started;

                -- Rule 6: every item gets its completion mode and party; the four new items are added to every open record.
                UPDATE "SeparationChecklists"
                SET "CompletionMode" = CASE "Step"
                                           WHEN 'DecisionIssued' THEN 'System'
                                           WHEN 'ContractTerminated' THEN 'System'
                                           WHEN 'EmployeeNotified' THEN 'Manual'
                                           ELSE 'PartyAck' END,
                    "PartyName" = CASE "Step"
                                      WHEN 'PayrollSettled' THEN 'Payroll'
                                      WHEN 'CardRevoked' THEN 'IDCard'
                                      WHEN 'AccountDisabled' THEN 'Auth.Directory'
                                      WHEN 'AssetsReturned' THEN 'Admin.Logistics'
                                      WHEN 'Archived' THEN 'Records.Archive'
                                      WHEN 'ItAccessRevoked' THEN 'IT.Infra'
                                      WHEN 'HrCoreUpdated' THEN 'HR.Core'
                                      ELSE NULL END;
                INSERT INTO "SeparationChecklists"
                    ("Id", "TenantId", "CreatedAt", "UpdatedAt", "SeparationRecordId", "Step", "IsCompleted", "IsRequired",
                     "SourceSystem", "OwnerRole", "SlaHours", "CompletionMode", "PartyName")
                SELECT md5(r."Id"::text || ':' || v.step)::uuid, r."TenantId", now(), now(), r."Id", v.step, false, true,
                       'HRContracts', v.owner, v.sla, v.mode, v.party
                FROM "SeparationRecords" r
                CROSS JOIN (VALUES
                    ('DecisionIssued', 'hr-contracts', 24, 'System', NULL),
                    ('EmployeeNotified', 'hr-contracts', 48, 'Manual', NULL),
                    ('ItAccessRevoked', 'it-infra', 48, 'PartyAck', 'IT.Infra'),
                    ('HrCoreUpdated', 'hr-core', 24, 'PartyAck', 'HR.Core')) AS v(step, owner, sla, mode, party)
                WHERE r."Status" IN ('Draft', 'PendingApproval', 'Approved', 'Executing', 'AwaitingIntervention')
                  AND NOT EXISTS (SELECT 1 FROM "SeparationChecklists" c WHERE c."SeparationRecordId" = r."Id" AND c."Step" = v.step);

                -- Rule 7: each record to the nearest Separation transaction of its employee created within 5 minutes of it, each
                -- transaction once (nearest pair first; ties by ids). What stays unlinked is sep060-report-unlinked.ps1's.
                LOOP
                    SELECT r."Id" AS record_id, t."Id" AS transaction_id INTO pair
                    FROM "SeparationRecords" r
                    JOIN "ServiceTransactions" t
                      ON t."TenantId" = r."TenantId" AND t."EmployeeId" = r."EmployeeId" AND t."TransactionType" = 'Separation'
                     AND t."SeparationRecordId" IS NULL
                    WHERE abs(extract(epoch FROM (t."CreatedAt" - r."CreatedAt"))) <= 300
                      AND NOT EXISTS (SELECT 1 FROM "ServiceTransactions" x WHERE x."SeparationRecordId" = r."Id")
                    ORDER BY abs(extract(epoch FROM (t."CreatedAt" - r."CreatedAt"))), r."Id", t."Id"
                    LIMIT 1;
                    EXIT WHEN NOT FOUND;
                    UPDATE "ServiceTransactions" SET "SeparationRecordId" = pair.record_id WHERE "Id" = pair.transaction_id;
                END LOOP;

                IF force_records THEN EXECUTE 'ALTER TABLE "SeparationRecords" FORCE ROW LEVEL SECURITY'; END IF;
                IF force_items THEN EXECUTE 'ALTER TABLE "SeparationChecklists" FORCE ROW LEVEL SECURITY'; END IF;
                IF force_sagas THEN EXECUTE 'ALTER TABLE "SeparationSagaStates" FORCE ROW LEVEL SECURITY'; END IF;
                IF force_transactions THEN EXECUTE 'ALTER TABLE "ServiceTransactions" FORCE ROW LEVEL SECURITY'; END IF;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SeparationRecordId",
                table: "ServiceTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "SeparationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DecidedAt",
                table: "SeparationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecidedBy",
                table: "SeparationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecisionId",
                table: "SeparationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutedAt",
                table: "SeparationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutedBy",
                table: "SeparationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLegacyDraft",
                table: "SeparationRecords",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "SeparationRecords",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "SeparationRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<int>(
                name: "SubmissionNo",
                table: "SeparationRecords",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "SeparationRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedBy",
                table: "SeparationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkflowCode",
                table: "SeparationRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowInstanceId",
                table: "SeparationRecords",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "SeparationRecords",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<string>(
                name: "CompletionMode",
                table: "SeparationChecklists",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<Guid>(
                name: "EvidenceFileId",
                table: "SeparationChecklists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExceptionApprovedAt",
                table: "SeparationChecklists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExceptionApprovedBy",
                table: "SeparationChecklists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionReason",
                table: "SeparationChecklists",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExceptionRequestedAt",
                table: "SeparationChecklists",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExceptionRequestedBy",
                table: "SeparationChecklists",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartyName",
                table: "SeparationChecklists",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // The new columns' defaults are for a row the previous tag may still insert after a rollback (it omits them): a Draft marked as
            // an older record, an item whose mode the start-up reconciler sets. This code always writes them.
            migrationBuilder.Sql(Backfill);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTransactions_SeparationRecordId",
                table: "ServiceTransactions",
                column: "SeparationRecordId",
                unique: true,
                filter: "\"SeparationRecordId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationRecords_DecisionId",
                table: "SeparationRecords",
                column: "DecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_SeparationRecords_Status_SubmittedAt",
                table: "SeparationRecords",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeparationRecords_Tenant_Employee_Open",
                table: "SeparationRecords",
                columns: new[] { "TenantId", "EmployeeId" },
                unique: true,
                filter: "\"Status\" IN ('Draft', 'PendingApproval', 'Approved', 'Executing', 'AwaitingIntervention')");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "TenantId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SeparationRecords_AdministrativeDecisions_DecisionId",
                table: "SeparationRecords",
                column: "DecisionId",
                principalTable: "AdministrativeDecisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SeparationRecords_AdministrativeDecisions_DecisionId",
                table: "SeparationRecords");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTransactions_SeparationRecordId",
                table: "ServiceTransactions");

            migrationBuilder.DropIndex(
                name: "IX_SeparationRecords_DecisionId",
                table: "SeparationRecords");

            migrationBuilder.DropIndex(
                name: "IX_SeparationRecords_Status_SubmittedAt",
                table: "SeparationRecords");

            migrationBuilder.DropIndex(
                name: "IX_SeparationRecords_Tenant_Employee_Open",
                table: "SeparationRecords");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SeparationRecordId",
                table: "ServiceTransactions");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "DecidedAt",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "DecidedBy",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "DecisionId",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "ExecutedAt",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "ExecutedBy",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "IsLegacyDraft",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "SubmissionNo",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "SubmittedBy",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "WorkflowCode",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "WorkflowInstanceId",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "SeparationRecords");

            migrationBuilder.DropColumn(
                name: "CompletionMode",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "EvidenceFileId",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "ExceptionApprovedAt",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "ExceptionApprovedBy",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "ExceptionReason",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "ExceptionRequestedAt",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "ExceptionRequestedBy",
                table: "SeparationChecklists");

            migrationBuilder.DropColumn(
                name: "PartyName",
                table: "SeparationChecklists");
        }
    }
}
