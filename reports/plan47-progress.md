Started (UTC): 2026-11-08T12:06:33.1641687Z

# PLAN-47 progress

## P0
- Preconditions: PLAN-46 PR 44 merged by this session on the owner's instruction at 2026-11-08T12:04:33Z (49a5f08d); merge note db25fae1 on docs/plan-46-merge-note.

- Branch feature/plan-47-pol-rule-lifecycle from origin/main 49a5f08d (upstream unset, never pushed to main). Commits: 76ee9ad4 (PLAN-46 merge note, cherry-picked), 03cefb23 (this prompt). Deleted branches: docs/plan-46-merge-note, docs/plan-35-final-report, docs/plan-35-post-merge, docs/pr28-post-merge (no commit outside main).
- Preconditions: ssh hrm-vps whoami = hrm, known host key matches (BatchMode, strict checking); gh active account omardefdev, scopes repo + workflow. Server tag dbf53280121d (on origin/main).
- Baseline build: dotnet build HRManagement.slnx -warnaserror, 0 warnings, 0 errors, 63 s.

## Notes (read once, do not re-read)
- National templates carry TenantId = TenantDefaults.DefaultTenantId (not NULL); TenantOverride templates carry the agency tenant.
- SeedAllDomainsAsync v1 rules that trigger PLAN-46 retired codes: R-HIRE-START (HIRE-WIZARD, Contracts.Hire), R-SEP-START (SEPARATION-WIZARD), R-TRF (TRANSFER-APPROVAL), R-TKT (IT-TICKET). WorkflowPackSeeder.RetiredCodes = those four + RECRUITMENT-HIRE. Bindings for all of them are seeded in PolicyEngineSeeder.WorkflowBindings.
- SeedIncrementalRulesAsync rules (to BASELINE change set): R-LEAVE-SELF, R-LEAVE-MAX (Contracts.Leave); R-REC-ORG, R-REQ-SUBMIT, R-REQ-PUBLISH-DENY, R-REQ-PUBLISH, R-OFF-OVERRIDE, R-OFF-EXCEPTION, R-OFF-ISSUE, R-COMMITTEE-SUBMIT, R-APPEAL-FILE, R-REC-HIRE-OFFER (Core.Recruitment); R-ARC-HOLD (Archive.Retention); R-CLASSIFY-HIGH-VALUE, R-CLASSIFY-CAPITAL (Assets.Custody, already in v1 on a fresh DB); R-PRIV-CODE (Assets.Custody, SeedSelectCodePolicyRuleIfMissingAsync). Facts fields via EnsureFactsField.
- Cache key is per day (yyyyMMdd): an explicit asOf inside today hits the "now" entry. P4 must key/bypass explicit asOf.
- PolicyVersionPublisher publishes IPolicyPublished {Domain, VersionId, PublishedAt} with DeterministicMessageId.For<IPolicyPublished>(version.Id) before SaveChanges.
- Server read-only (2026-11-08T12:3xZ): 20 templates, all National (DefaultTenantId 00000000-0000-4000-8000-000000000001), each only v1 Published; 20 publications, none with maker or checker; no draft. No row named "[معطّلة]..." and no R-REC-HIRE row (the server DB was seeded after PLAN-25). Rules that trigger PLAN-46 retired codes (v1 of each): R-TRF (TRANSFER-APPROVAL), R-SEP-START (SEPARATION-WIZARD), R-HIRE-START (HIRE-WIZARD), R-TKT (IT-TICKET). Fingerprint: artifacts/plan47/vps/fingerprint-p0.json (20 versions, 37 rules). Detail: artifacts/plan47/vps/p0-policy-state.txt.
- Row security (server): all eight Policy* tables ENABLE + FORCE; policy tenant_isolation = ("TenantId")::text = NULLIF(current_setting('app.current_tenant', true), '') for USING and WITH CHECK. Owner hr_core_migrator (not super, no BYPASSRLS); app role hr_core_app (not super, no BYPASSRLS). A national row is visible only in a session whose tenant is the default tenant; a tenant session does not see national rows.
- Trigger design consequence: the rule guard looks up its version as the invoking role. A rule and its version share a tenant, and every write runs in that tenant's session, so the version is visible; a version the lookup cannot see is refused (fail closed) except on DELETE where the version is already gone (cascade from a discarded draft).
- Code inventory: artifacts/plan47/inventory-code.md. Creators of Published versions that insert rules in the same save (would be refused by the guards): SeedAllDomainsAsync, SeedAssetsCustodyAsync, AcceptanceDataSeeder (v2), AcceptanceS2s42PolicyPatch (v4), AgencyPolicySeeder. Plan: create as Draft + rules, save, then publish (Draft -> Published is a change of a draft row).
- Gateway today sends every hr-core POST to HrCoreWrite, so an hr-management-approver cannot reach publish through the gateway.
- Live realm hrmanagement (read-only): no policy-author / policy-approver role; e2e-checker-mgmt = hr-core + hr-management-approver; hradmin, e2e-hr-admin = hr-admin. (artifacts/plan47/vps/p0-realm.txt)
- Demo server HR.Core connects as the PostgreSQL superuser (docker-compose.vps-demo.yml): row security does not filter there; triggers still fire.
- Baseline (reports/plan47-baseline.md): pending model changes none; unit filter 353/353; integration filter 6/6.
- Inventories: artifacts/plan47/inventory-code.md, artifacts/plan47/inventory-rls.md (two read-only subagents, about 6.5 and 8.3 min).
- Decision (trigger): SECURITY INVOKER status lookup, fail closed when the version is not visible; proved in P5 as a non-superuser under FORCE for a national (PLATFORM) and a tenant template.
- Decision (creators of published versions): insert the version as Draft with its rules, save, then publish; never insert rules into a published version.
Finished P0 (UTC): 2026-11-08T12:17:16.7553872Z
- Next step: P1 (PolicyRuleJson strict parser, PolicyRuleValidator, IPolicyDomainCapabilities, invariant WorkflowCondition with LessThanOrEqual, mapper Never + InvalidRuleKeys).
