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
