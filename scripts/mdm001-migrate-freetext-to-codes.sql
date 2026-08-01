-- MDM-001: migrate free-text position titles to reference catalog codes.
-- Run AFTER seeding ReferenceCatalogEntries with JobClassification rows.
-- This is a best-effort fuzzy match; review results before committing.

BEGIN;

-- Map EmploymentRecords.PositionTitle → JobClassificationCode via NameAr partial match
UPDATE "EmploymentRecords" er
SET "JobClassificationCode" = ref."Code"
FROM "ReferenceCatalogEntries" ref
WHERE ref."CatalogKind" = 'JobClassification'
  AND ref."IsActive" = true
  AND er."JobClassificationCode" IS NULL
  AND (
      er."PositionTitle" ILIKE '%' || ref."NameAr" || '%'
      OR er."PositionTitle" ILIKE '%' || ref."NameEn" || '%'
  );

-- Map CareerPathSteps.PositionTitle → JobClassificationCode via NameAr partial match
UPDATE "CareerPathSteps" cs
SET "JobClassificationCode" = ref."Code"
FROM "ReferenceCatalogEntries" ref
WHERE ref."CatalogKind" = 'JobClassification'
  AND ref."IsActive" = true
  AND cs."JobClassificationCode" IS NULL
  AND (
      cs."PositionTitle" ILIKE '%' || ref."NameAr" || '%'
      OR cs."PositionTitle" ILIKE '%' || ref."NameEn" || '%'
  );

-- Verification queries
SELECT 'EmploymentRecords' AS "Table",
       COUNT(*) FILTER (WHERE "JobClassificationCode" IS NOT NULL) AS "Mapped",
       COUNT(*) FILTER (WHERE "JobClassificationCode" IS NULL) AS "Unmapped",
       COUNT(*) AS "Total"
FROM "EmploymentRecords";

SELECT 'CareerPathSteps' AS "Table",
       COUNT(*) FILTER (WHERE "JobClassificationCode" IS NOT NULL) AS "Mapped",
       COUNT(*) FILTER (WHERE "JobClassificationCode" IS NULL) AS "Unmapped",
       COUNT(*) AS "Total"
FROM "CareerPathSteps";

-- Uncomment to commit:
-- COMMIT;
-- For dry-run inspection, roll back:
ROLLBACK;
