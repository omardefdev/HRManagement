-- HCM-030 (C1, AC-HCM-30j): link requisition-linked channel applications submitted before unification.
--
-- For every CitizenApplications / InternalApplications row with a RequisitionId and no
-- PromotedRecruitmentApplicationId, insert the RecruitmentApplication it would have had (IntakeChannel Public /
-- Internal, AppliedAt = the source submission time, TenantId of the source) and set the existing link column.
-- Citizen COI fields are copied. Nothing else is updated or deleted.
--
-- * Transactional: one BEGIN … COMMIT. scripts/hcm030-backfill-unified-intake.ps1 -DryRun (default) runs it with
--   the final COMMIT replaced by ROLLBACK and reports what would change.
-- * Idempotent: a second run finds no unlinked row and inserts nothing.
-- * Status mapping = HR.Core.Domain.Services.UnifiedIntakeStatusMapping (C#); Hcm030BackfillMappingTests checks
--   both against the same table. Rejection dates come from the source row or the requisition, never now(), so no
--   appeal window reopens.
-- * Raw SQL: no RecruitmentApplicationEvents rows are written, so the candidate notifications (PLAN-30 session 2)
--   are never sent for these historic applications.
-- * Internal rows: Employees.NationalId is encrypted at rest, so only the hash is copied (NationalId stays NULL).
-- * Eligibility flags are not evaluated (historic rows carry no profile data): EligibilityFlags stays NULL.

BEGIN;

-- Keep new channel rows and manual links (PromoteFrom*) out while the plan is built and applied.
LOCK TABLE "CitizenApplications", "InternalApplications" IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE hcm030_plan ON COMMIT DROP AS
WITH src AS (
    SELECT 'Citizen'::text                                   AS source_table,
           c."Id"                                            AS source_id,
           c."TenantId"                                      AS tenant_id,
           c."RequisitionId"                                 AS requisition_id,
           c."Status"                                        AS source_status,
           c."UpdatedAt"                                     AS source_updated_at,
           c."SubmittedAt"                                   AS applied_at,
           c."FullName"                                      AS full_name,
           c."NationalId"                                    AS national_id,
           c."NationalIdHash"                                AS national_id_hash,
           c."ConflictOfInterestDeclared"                    AS coi_declared,
           c."ConflictOfInterestDeclaredAt"                  AS coi_declared_at,
           'تقديم عبر البوابة العامة ' || c."ReferenceNumber" AS base_note,
           'Public'::text                                    AS channel,
           NULL::uuid                                        AS source_internal_id
    FROM "CitizenApplications" c
    WHERE c."RequisitionId" IS NOT NULL
      AND c."PromotedRecruitmentApplicationId" IS NULL
    UNION ALL
    SELECT 'Internal', i."Id", i."TenantId", i."RequisitionId", i."Status", i."UpdatedAt", i."AppliedAt",
           e."FullName", NULL, e."NationalIdHash", false, NULL, 'ترشّح داخلي', 'Internal', i."Id"
    FROM "InternalApplications" i
    JOIN "Employees" e ON e."Id" = i."EmployeeId"
    WHERE i."RequisitionId" IS NOT NULL
      AND i."PromotedRecruitmentApplicationId" IS NULL
),
mapped AS (
    SELECT src.*,
           r."Status"                                                     AS requisition_status,
           CASE WHEN nullif(trim(r."JobTitle"), '') IS NULL THEN '—' ELSE trim(r."JobTitle") END AS applied_position,
           -- 0 Received/Submitted, 1 UnderReview, 2 Shortlisted, 3 Accepted, 4 Rejected
           src.source_status <> 4 AND r."Status" IN ('Cancelled', 'Closed') AS requisition_ended,
           CASE WHEN r."Status" = 'Cancelled' THEN coalesce(r."CancelledAt", r."ClosedDate") ELSE r."ClosedDate" END AS ended_at
    FROM src
    JOIN "RecruitmentRequisitions" r ON r."Id" = src.requisition_id
)
SELECT gen_random_uuid() AS new_id,
       m.*,
       CASE
           WHEN m.source_status = 4 OR m.requisition_ended THEN 3   -- Rejected
           WHEN m.source_status = 1 THEN 1                          -- UnderReview
           WHEN m.source_status IN (2, 3) THEN 2                    -- Shortlisted
           ELSE 0                                                   -- Submitted
       END AS new_status,
       CASE
           WHEN m.source_status = 4 THEN 'SelectionDecision'
           WHEN m.requisition_ended AND m.requisition_status = 'Cancelled' THEN 'RequisitionCancelled'
           WHEN m.requisition_ended THEN 'RequisitionClosed'
       END AS rejection_kind,
       CASE
           WHEN m.source_status = 4 THEN m.source_updated_at
           WHEN m.requisition_ended THEN coalesce(m.ended_at, m.source_updated_at)
       END AS rejected_at,
       CASE WHEN m.source_status = 1 AND NOT m.requisition_ended THEN m.source_updated_at END AS screened_at,
       CASE
           WHEN m.source_status = 4 THEN m.source_updated_at
           WHEN m.requisition_ended THEN coalesce(m.ended_at, m.source_updated_at)
           WHEN m.source_status = 0 THEN NULL
           ELSE m.source_updated_at
       END AS reviewed_at,
       m.base_note || CASE
           WHEN m.source_status = 3 AND NOT m.requisition_ended
               THEN ' — كان مقبولاً في قناة التقديم قبل توحيد المسار.'
           ELSE ''
       END AS notes
FROM mapped m;

INSERT INTO "RecruitmentApplications" (
    "Id", "TenantId", "ApplicantFullName", "NationalId", "NationalIdHash", "LegalHold", "AppealStatus",
    "AppliedPosition", "RequisitionId", "SourceInternalApplicationId", "AppliedAt", "Status", "Notes",
    "IsLegacyUnlinked", "ReviewedAt", "ScreenedAt", "ConflictOfInterestDeclared", "ConflictOfInterestDeclaredAt",
    "RejectedAt", "RejectionKind", "IntakeChannel", "CreatedAt", "UpdatedAt")
SELECT p.new_id, p.tenant_id, p.full_name, p.national_id, p.national_id_hash, false, 'None',
       p.applied_position, p.requisition_id, p.source_internal_id, p.applied_at, p.new_status, p.notes,
       false, p.reviewed_at, p.screened_at, p.coi_declared, p.coi_declared_at,
       p.rejected_at, p.rejection_kind, p.channel, now(), now()
FROM hcm030_plan p;

UPDATE "CitizenApplications" c
SET "PromotedRecruitmentApplicationId" = p.new_id
FROM hcm030_plan p
WHERE p.source_table = 'Citizen' AND c."Id" = p.source_id AND c."PromotedRecruitmentApplicationId" IS NULL;

UPDATE "InternalApplications" i
SET "PromotedRecruitmentApplicationId" = p.new_id
FROM hcm030_plan p
WHERE p.source_table = 'Internal' AND i."Id" = p.source_id AND i."PromotedRecruitmentApplicationId" IS NULL;

-- Report (one row per linked application): read by the PowerShell wrapper for the CSV and the per-mapping counts.
SELECT p.source_table, p.source_id, p.tenant_id, p.requisition_id, p.source_status, p.requisition_status,
       p.new_status, coalesce(p.rejection_kind, ''), p.new_id
FROM hcm030_plan p
ORDER BY p.source_table, p.tenant_id, p.source_id;

COMMIT; -- HCM030:END
