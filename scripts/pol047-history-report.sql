-- PLAN-47 (P-3): the policy rules PLAN-25's DeactivateRuleIfPresentAsync rewrote in place before PLAN-47 (names that start
-- with "[<deactivated>] "). They stay as they are; this lists them for the audit note of the final report. Read-only.
-- U&'\0645\0639\0637\0651\0644\0629' is the Arabic word for "deactivated", written in ASCII.
SELECT t."Domain", t."Scope", t."TenantId", v."VersionNumber", v."Status", r."RuleKey", r."Name", r."WhenJson", r."ThenJson"
FROM "PolicyRules" r
JOIN "PolicyTemplateVersions" v ON v."Id" = r."PolicyTemplateVersionId"
JOIN "PolicyTemplates" t ON t."Id" = v."PolicyTemplateId"
WHERE r."Name" LIKE '[' || U&'\0645\0639\0637\0651\0644\0629' || ']%'
ORDER BY t."Domain", t."TenantId", v."VersionNumber", r."RuleKey";
