Started (UTC): 2026-11-09T15:27:17.5274608Z

# PLAN-48 progress (checkpoint)

## Session notes (read on resume)
- Preconditions (P0, 2026-11-09 ~15:30Z): main folder, branch from origin/main 61816ed7; tree clean except global.json, tmp-vendors.html, the prompt; ssh hrm-vps whoami = hrm, host key matches the plan; gh = omardefdev (repo, workflow).
- Deviation from the prompt's facts: the demo server already runs 61816ed7dda2 (PLAN-47's last fix deployed after the merge, per the merge note), not ae95ec2fec82.
- PLAN-47's merge note was two commits (32e00c1f, c185c765), both cherry-picked (1405ccd1, dbd19381); branch docs/plan-47-merge-note deleted after git cherry showed both applied.
- Prompt committed: ac5629e8.
- Leftover hrm-acceptance stack (PLAN-47 run, up since 2026-11-08T14:45Z) read for the baseline before removal: artifacts/plan48/baseline-acceptance-*.txt.
- Stray container nostalgic_shannon (mcr dotnet/sdk:8.0, no labels) is NOT this repo's: leave it.
- Restoring test-rewritten outputs: for each path in `git diff --name-only -- testdata | grep /out/`: `rm -f <path>; git checkout -- <path>` (a plain checkout leaves CRLF stat noise; the quoted testdata/*/out pathspec does not match in Git Bash).

## P0 — Timing, preconditions, baseline, inventories
Status: done. Finished (UTC): 2026-11-09T16:02:11.8044909Z
- Preconditions 1-5 pass (see Session notes).
- Baseline (0.2): build 61 s (0 errors); unit filter 437/437 (127 s); integration filter (Testcontainers) 19/19 (101 s).
- Inventories: two read-only agents in one workflow, 28 min (15:45-16:13Z), artifacts/plan48/inventory-*.md (not committed).
- Server read-only queries: artifacts/plan48/server-p0*.txt; ~1 min.
- Summary in reports/plan48-baseline.md.

### Decisions (with reasons)
- D-P0-1 Citizen references stay ONE national sequence (issued in the national tenant), not per posting tenant: CitizenApplications.ReferenceNumber is unique across tenants (HRCoreDbContext:723) and the portal finds an application by the reference alone (OTP verify); per-tenant CIT counters would collide on that index. Deviation from plan section 2 / prompt P2 ("/careers with the posting's tenant"); the prefix defect is fixed either way (the generator reads national policies through OwnOrNational).
- D-P0-2 AC-13 keeps the PLAN-42 service path (prompt P-7), so its third kind is ArchiveReference (svc-records-archive, two entity prefixes) instead of RecruitmentRequisition, which has no service owner in CodeKindOwners and cannot go through api/internal/codes/generate. The requisition's two-budget-year scopes are proved in a PostgreSQL parallel test (Ten048FundedPositionRealCodeServiceTests / requisition) and in AC-TEN-48b. Deviation recorded.
- D-P0-3 The NationalSharedWriteGuard lives in ServiceDefaults (TenantStampScope.IsExplicitAllowed is internal there) and is registered inside HRCoreDbContext (OnConfiguring), so every host and test context gets it; it runs after the DI interceptors (after the tenant stamp).
- D-P0-4 PublishedPolicyImmutabilityInterceptor's lookup stays session-scoped (EF filter), not national: an agency rule on a national draft stays refused, as the POL047 trigger's FOR SHARE lookup refuses it.
- D-P0-5 Agency default code overlays are stored as the kind's default template (PolicyKey null) when the pack says "default": true; no new column (no EF migration).

## Next step
P1: finish the read path (remaining 3a reads: policy pages/API, workflow definitions API, reference data controller, EstablishmentPayloadSummaryBuilder, WorkflowDefinitionHealthReport, WorkflowPackSeeder reads, MdmReferenceSeeder source), register the guard in HRCoreDbContext, then NationalSharedArchitectureTests and Ten048NationalRlsIntegrationTests; commit P1 with only P1 files staged (P2/P3 code-generation files are already in the working tree, uncommitted).
- (P1 commit isolation) The eight P2/P3 code-generation files were set aside in the session scratchpad (scratchpad/p23) while P1 is built, tested and committed; restore them right after the P1 commit if resuming.

## P1 — The national read: EF and row security
Status: done. Finished (UTC): 2026-11-09T16:26:42.4809577Z
- Markers and list: INationalShared on the ten entities; NationalTenant.TableNames; SystemTenantBypassPurpose.NationalSharedRead.
- Read path: NationalScopeQueryExtensions (OwnOrNational(tenant), OwnOrNational(session), NationalOnly, OwnFirst, PreferOwn); NationalReadQueries (scoped tables: own, or national with Scope = National). All 20 PolicyResolution/NationalReferenceLookup uses replaced; also the policy pages and API reads, the workflow definitions API, the reference data API, EstablishmentPayloadSummaryBuilder, WorkflowDefinitionHealthReport, WorkflowPackSeeder (NationalOnly), MdmReferenceSeeder source, the acceptance patches. PublishedPolicyImmutabilityInterceptor lookup is now session-scoped (no bypass). No general bypass on a national table remains (architecture test).
- Write guard: NationalSharedWriteGuard (ServiceDefaults), registered in HRCoreDbContext.OnConfiguring; NationalSharedWriteRefusedException : UnauthorizedAccessException.
- Policy: TenantRlsMigrationHelper.NationalSharedReadSql + VerifyNationalSharedReadSql; scripts/sql/ten048-national-shared-read.sql (apply + check block); applied by DatabaseMigrationExtensions (contexts with INationalShared entities), scripts/run-migrations.sh (hr-core) and deploy/bootstrap/bootstrap.sh (hr-core, the VPS path).
- Decision D-P1-1: OwnOrNational(Guid.Empty) is an ordinary tenant value (only null means no tenant); OwnOrNational(session) mirrors the EF filter for design-time contexts (no tenant context reads everything), so in-memory tests keep their semantics. Production contexts always carry a tenant context.
- Decision D-P1-2: WorkflowPackSeeder's BackfillGovernance/Retire read national definitions only (they read every tenant before; agencies cannot author definitions, so nothing else changes).
- Test changes (P-8): Est007ReferenceCatalogAdminTests seed their national rows inside TenantStampScope (an explicit seed), assertions unchanged; Ops038ReviewFindingsTests counts five PGPASSWORD uses in run-migrations.sh (the new TEN-048 psql call), not four.
- Tests: unit full suite 5649/5649 (97 s) on the P1 state; NationalShared* 46 incl. tenancy filters; integration (Testcontainers) Ten048|CrossTenantRls|Pol047|Wf04|Ops038|Mdm|CrossTenant|Ten00: 106 passed, 1 skipped (123 s). Ten048NationalRlsIntegrationTests: 6 tests as hrm_rls_app (NOSUPERUSER NOBYPASSRLS) under FORCE.
- (P2 commit isolation) P3 files set aside in scratchpad/p3; restore after the P2 commit.

## P2 — Code policies for every tenant, no silent fallback
Status: done. Finished (UTC): 2026-11-09T17:32:31.1524724Z
- CodeGeneratorService.ResolveAsync through OwnOrNational: (1) the tenant's overlay (TenantOverride), (2) the national template (TenantId PLATFORM and Scope National), (3) none; same kind and PolicyKey (no key = the kind's default template; a request without a key never gets a keyed template; an unknown key is CodePolicyMissingException). A template with no version in force is passed over; an invalid published version is refused (CodePolicyInvalidException, logged).
- MissingPolicy (CodeGenerationOptions, D-TEN-9): Refuse everywhere except Development/Testing (Fallback = {kind prefix}-YYYY-#####, never empty); configuration can only make it stricter; no environment = Refuse. The check comes before any counter moves.
- A fact the number shows is required (CodeFactMissingException), never an empty segment. Guid.Empty as a request tenant means the session tenant.
- NationalScopeErrorFilter (HR.Core.Web, global): 422 code-policy-missing/invalid/fact-missing, 409 code-collision, 403 national-shared-write; ProblemDetails for API, shared error view for pages.
- Snapshot cache key includes PolicyKey; a generation counter invalidates every entry on publish/upgrade/overlay (CodeGeneratorService.InvalidateSnapshotCache). ICodeGenerator.GetPublishedSnapshotAsync gains an optional policyKey; api/codes/policies/{kind} answers for the session tenant unless the caller is a platform operator.
- /CodePolicies and api/codes/policies: ReadableCodePolicyTemplates (own overlays + national Scope National); badges «وطني (موروث — للقراءة)», «تجاوز الجهة», «وطني»; the preview passes the row's key and shows a missing fact as a message.
- Citizen references: CodeGenerationRequests.CitizenReference() issues in the national tenant explicitly (D-P0-1 deviation). RecruitmentRequisition's BudgetYear is formatted invariant.
- Code overlays: AgencyCodePolicySeeder honours "default": true (stored as the agency's default template, key kept in the description) and "sequenceReset"; validates every policy and rule 5 before saving, refuses an invalid pack whole; idempotency read with an explicit tenant predicate. code-ministry-finance-v1.json: "default": true, "sequenceReset": "PerScope" (FIN-ORG-UNIT stays the finance agency's OrgUnit default, no yearly restart).
- The generator needs the format, the scope key and the validator, so CodeFormat, CodeScopeKey and CodePolicyValidator land in P2 (with the corrected seeder catalog, CodeGenerationSeeder.NationalPolicies), not P3. P3 brings the upgrade, the reseeder, the bootstrap rebuild, the funded-position service fix, the orchestrator wiring and their tests.
- scripts/ten048-find-prefixless-codes.ps1 (ASCII, PS 5.1; -Target Acceptance|Server, -Since, -FailIfAny; report-only to artifacts/ten048/prefixless-codes.csv). Read-only run on the server: 0 prefixless across 12 number columns in 7 databases (~20 s).
- Test changes (P-8): CodeGenerationTests (3 tests generated with no policy, relying on the silent fallback) now seed the national catalog first; assertions unchanged. CodeGenerationConcurrencyTests and S2s042CodeIssuanceTenantPostgresTests seeded the national catalog from an agency context: now inside TenantStampScope (an explicit seed); assertions unchanged. These two failed since P1 (the guard) and were not in P1's integration filter: P1's commit e61ff9c8 left them red; fixed here.
- Tests: Ten048ResolutionTests (14 incl. theory rows); unit full suite on the P2 state 5663/5663 (115 s); integration CodeGeneration|PublicCareers|S2s042|Est00|Hcm031|Wf044|Ten048|Recruitment|Hcm03|Int037|Prod001: 89 passed, 2 skipped (95 s).

## P3 — The validator, the corrected policies, one scope key
Status: done. Finished (UTC): 2026-11-09T17:49:06.3555990Z
- (CodeFormat, CodeScopeKey, CodePolicyValidator and the corrected catalog landed with P2, see there.)
- CodePolicyUpgrades (once, SeedPackApplications 'ten048-code-policy-upgrade-v1'): FundedPosition v1 (scope [OrgUnitCode, GradeCode, BudgetYear]) -> v2 scope [OrgUnitCode, GradeCode] (D-TEN-6); RecruitmentRequisition v1 REQ-{YYYY} Yearly -> v2 REQ-{BudgetYear} PerScope [BudgetYear] (D-TEN-5); matched by a normalised comparison of segments and reset; v1 Archived with EffectiveTo now, v2 Published from now; then every tenant reseeded; then recorded. Called from both paths of NationalSeedOrchestrator (on a new database it only records itself, so the orchestrator stays idempotent from its first run). CostCenter (derived) and ASSET-PRIVILEGED (own counter through the key) need no new version.
- CodeCounterReseeder: tenants from Tenants (outside row security); per tenant TenantIdFlow then a new DI scope (its own session); reads CodeIssuances (with facts and the policy version) and the HR.Core number columns (positions with unit and grade facts, org units with the parent's code, job catalog with the grade, requisitions, offers, citizen applications, competitions, establishment change requests); parses with the formats in force (overlay first, then national; known facts first, text second); raises with INSERT ... ON CONFLICT DO UPDATE SET LastValue = GREATEST(...): never lower; unparsed codes reported, never guessed; a tenant failure is reported and fails the run. Without an HRCoreDbContext registration it reseeds through the given context only for a design-time context (tests, tools); otherwise it refuses.
- The demo and acceptance seed orchestrators reseed after their seeds (they write numbers themselves: PLAN-43 §10's class of defect).
- CodSequenceBootstrapService rebuilt on CodeFormat/CodeScopeKey (formats in force, or the national catalog when none is published yet); BootstrapHRCoreAsync = the reseeding of one tenant.
- FundedPositionCodeService: a draft with Guid.Empty is numbered in the session's tenant; BudgetYear invariant. The cost centre is derived and issued once (ON CONFLICT DO NOTHING).
- Start-up log of the kinds with no national policy in force (ForensicCase expected).
- Defect found and fixed by the PostgreSQL test: the upgrade added v2 through the tracked template's collection; with BaseEntity's preset Guid key EF took it for an existing row and updated nothing (DbUpdateConcurrencyException). v2 is now added explicitly.
- Test infrastructure: the parallel PostgreSQL tests cap their pool at 8 and clear their pools; uncapped, 60 parallel sessions exhausted the shared container (53300) and failed 27 unrelated tests of the collection.
- Test changes (P-8): NationalSeedTests registers HRCoreDbContext in its service provider (the orchestrator reseeds per tenant scope); assertions unchanged. P-7: Est002/Est005/Est024 never number funded positions (their fakes stay); Est002 gains Apply_create_funded_position_numbers_with_the_real_service (two positions of one unit and one in the next budget year through the applier and the real service).
- Rollback finding (P-5): the previous tag reads the v2 versions (v1 is Archived) and builds the same keys for the corrected kinds, so their counters continue. It shares the AssetTag counter between AH and VIP again: a VIP number issued after the upgrade can collide under it once the VIP counter passes the AH counter. Roll forward; before a rollback raise the shared AssetTag counter to the VIP counter (SQL in OPS_VPS_SINGLE_HOST.md, P8).
- Tests: CodePolicyValidatorTests, CodeFormatTests, CodeScopeKeyTests, Ten048DerivedCostCenterTests, Ten048CodeCollisionTests (SQLite: 409 class, then the reseeding clears it); unit full suite 5713/5713 (101 s); integration Ten048|CodeGeneration|S2s042|Est00|Hcm03|PublicCareers|Wf044|Recruitment|NationalSeed|Pol047|Ops038|CrossTenantRls|Int037|Prod001: 136 passed, 3 skipped (112 s), incl. Ten048CodePolicyUpgradeIntegrationTests (two tenants, hrm_rls_app under FORCE + national_shared_read) and Ten048FundedPositionRealCodeServiceTests (24 positions and 60 requisitions in parallel).
