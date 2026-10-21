SELECT 'workflows' AS metric, COUNT(*)::text AS value FROM "WorkflowDefinitions"
UNION ALL
SELECT 'mdm', COUNT(*)::text FROM "ReferenceCatalogEntries"
UNION ALL
SELECT 'bindings', COUNT(*)::text FROM "PolicyWorkflowBindings"
UNION ALL
SELECT 'seed_packs', COUNT(*)::text FROM "SeedPackApplications";

SELECT "Code" FROM "WorkflowDefinitions" ORDER BY "Code";

SELECT "CatalogKind", COUNT(*) AS cnt FROM "ReferenceCatalogEntries" GROUP BY "CatalogKind" ORDER BY 1;

SELECT "PackId", "ReleaseDigest", "AppliedAt" FROM "SeedPackApplications";
