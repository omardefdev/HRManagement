-- PLAN-47 (P-9): a fingerprint of every published policy version.
-- One row per version: SHA-256 over its rules' (RuleKey, WhenJson, ThenJson, Priority), ordered by RuleKey.
-- Read-only. Run as a role that sees every tenant (the stack's superuser), so row security hides nothing.
SELECT COALESCE(json_agg(x ORDER BY x."Domain", x."Scope", x."TenantId", x."VersionNumber"), '[]'::json)
FROM (
    SELECT v."Id" AS "VersionId",
           t."Domain",
           t."Scope",
           t."TenantId",
           v."VersionNumber",
           v."Status",
           count(r."Id") AS "RuleCount",
           encode(sha256(convert_to(COALESCE(string_agg(
               COALESCE(r."RuleKey", '') || chr(31) || r."WhenJson" || chr(31) || r."ThenJson" || chr(31) || r."Priority"::text,
               chr(30) ORDER BY r."RuleKey", r."Id"), ''), 'UTF8')), 'hex') AS "RulesSha256"
    FROM "PolicyTemplateVersions" v
    JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
    LEFT JOIN "PolicyRules" r ON r."PolicyTemplateVersionId" = v."Id"
    WHERE v."Status" = 'Published'
    GROUP BY v."Id", t."Domain", t."Scope", t."TenantId", v."VersionNumber", v."Status"
) x;
