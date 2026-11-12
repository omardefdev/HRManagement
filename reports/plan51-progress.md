Started (UTC): 2026-11-11T23:26:13.6316983Z

# PLAN-51 progress (checkpoint)

Prompt: docs/acceptance-plans/execution/PLAN-51-AGENT-PROMPT.md. Plan: docs/acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-11T23:45:16Z |
| P1 | done | 2026-11-11T23:55:59Z |
| P2 | done | 2026-11-12T00:06:52Z |
| P3 | done | 2026-11-12T00:18:21Z |
| P4 | done | 2026-11-12T00:46:32Z |
| P5 | done | 2026-11-12T01:07:30Z |
| P6 | done | 2026-11-12T03:10:24Z |
| P7 | done (no merge: origin/main still 0db7292e) | 2026-11-12T03:10:48Z |

## Session facts

- Keep-awake: hidden PowerShell `artifacts/plan51/keepawake.ps1` (SetThreadExecutionState ES_CONTINUOUS|ES_SYSTEM_REQUIRED), PID 13008, started 2026-11-11T23:27Z. Stop it at the end of the session.
- Branch `feature/plan-51-iam-explicit-policy-every-action` from `origin/main` (`0db7292e`), upstream unset. PLAN-50 merge note cherry-picked (`06d127dd`), prompt committed (`c1f60b3e`), local branch `docs/plan-50-merge-note` deleted (its only commit's content is on this branch).

## Decisions

1. **SSH to the demo server timed out at P0 (precondition 4).** Outbound TCP 22 is blocked from this machine's network at the moment (also to github.com:22), while the server answers ping and TCP 443. The server is first needed in P8, so the local phases go on; SSH is re-checked during the run, the P0 server read-only picture is taken as soon as it answers, and P8 stops with `BLOCKED:` if it still does not.
2. **The shared `CultureController`** (ServiceDefaults, `POST api/culture`) is discovered by every service and the gateway; the architecture catalog scans it as a sixteenth entry, `shared`, so no write in a shared controller escapes the lists.

## Timing

- P0 inventories: 0.4 by reflection (the P1 scanner; first cold run 2 min 24 s, warm 124 ms), 0.5 and 0.6 by two read-only agents (13 min and 8 min, in parallel), 0.7 from the 0.4 data. Baseline unit filter 486/486 in 26 s.

- P0 build (`dotnet build HRManagement.slnx -warnaserror`, incremental): 1 min 02 s, 0 warnings, 0 errors.

## Open issues

- SSH (decision 1).

## Next step

P7: check origin/main (merge only if it moved). Then P8 needs SSH to hrm-vps (blocked since P0: outbound TCP 22 times out); if it still does, BLOCKED with the question. P9 docs are written (uncommitted): GATEWAY_RUNBOOK, SERVICE_TRUST_INVENTORY, ESS_001, HIRE_RBAC_MATRIX, SEED_001, CI_AND_TESTING, the study status lines; still to write: OPS_VPS_SINGLE_HOST (P8), the plan status and notes, the final report.

## P0 summary (Finished (UTC): 2026-11-11T23:45:16Z)

- Baseline in `reports/plan51-baseline.md`: build 0/0; unit filter 486/486; 688 writes in the fifteen assemblies, 323 without a policy
  (+9 with roles only, +1 shared `Culture.SetCulture`); HR.Core's 42 = the old list; 125 entity bindings (12 in HR.Movements).
- 0.5: `hr-core.workflow` needed on the three endpoints only; no service account writes through `HrCore.Write`; the workflow-instances
  controller leans on the module filter's `Module.HrCore` (gets `[ModulePolicyOverride(Workflow.ExternalStart)]`).
- 0.6: no employee or marker page calls the lookup API; they need HR.Core's static files (`Gateway.Authenticated`).
- Server picture: pending (SSH, decision 1).

## P1 summary (Finished (UTC): 2026-11-11T23:55:59Z)

- `src/Tests/HRManagement.ArchitectureTests` (in the slnx; step "Architecture tests" in `scripts/ci/steps.json` and `ci.yml` after
  "Unit tests", no coverage collector): `WebAssemblyCatalog` (15 web assemblies + `shared` = ServiceDefaults' controllers),
  `ActionCatalog` (reflection, MVC's rules), `AllWebAssembliesLoadTests`, `WriteActionsHaveExplicitPolicyTests` (per-service lists and
  pinned ceilings, PLAN-25's recruitment rule), `SensitiveActionsHaveApprovalPolicyTests` (whole names, the plan's prefixes and named
  list), `InternalRoutesAreServiceOnlyTests` (moved from `S2s042…`), `ModuleOverridesAreDeclaredTests`,
  `MassAssignmentArchitectureTests` (moved, all fifteen assemblies), `GuardSelfTests` (planted defects). 128/128 green.
- Lists seeded: `tests/architecture/write-policy-allowlist/*.txt` (332 rows + 1 shared; Payroll's 16 signed PLAN-53, the rest
  PLAN-51), `sensitive-actions-allowlist.txt` (39, PLAN-51), `entity-binding-allowlist/*.txt` (125: HR.Movements 12 PLAN-51, 113
  IAM-F). A reason must start with an owning plan; `CeilingList.Owners` loses PLAN-51 in P4.
- Planted list defects (a removed row, an extra row) fail the tests; restored.
- `scripts/classify-test-evidence.py` counts the new project as architecture fitness.
- Timing: the project compiles in 5 to 24 s; a warm run takes 1.4 s (tests 0.4 s); the first run after a build takes about 2 min 25 s on
  this machine, outside the tests (host start-up over the freshly built assemblies).

Decisions:
3. **HR.Core's five sensitive writes that carry `HrCore.Write`** (`Competitions.PublishResults`, `OfferLetters.Revoke`,
   `RecruitmentRequisitions.Publish/PublishInternal`, `WorkforcePlans.Approve`) fail the plan's own sensitive rule, and `HrCore.Approve`
   would widen them (it admits the approver and platform-admin). P4 gives them a configurable `HrCore.Decision` with today's roles
   (hr-admin, hr-core), so nothing widens and the policy can be separated by configuration (D-51-2's pattern).
4. **`Authorize(Roles=…)` alone is not a named policy**: the nine roles-only writes (Compliance DSAR, gateway platform audit and
   tenant entry) are seeded and get named policies with the same roles in P4.

## P2 summary (Finished (UTC): 2026-11-12T00:06:52Z)

- `GatewayEssOptions` (`Gateway:Ess`, defaults in code = D-51-1) and `GatewayEssRouting.Decide`: the first segment after
  `/ui/{segment}/` or `/api/{segment}/`, exact (case-insensitive like MVC routes). On `ess` the decision is final: a named controller
  gets `Module.Ess`, a static file (GET/HEAD, no traversal) `Gateway.Authenticated`, anything else 404 before forwarding with the audit
  decision `Denied:EssNotExposed` (handed to `GatewayAuditMiddleware` through `HttpContext.Items`). `TryGetEssPolicy` (`Contains("/Ess")`)
  removed.
- `HrCore.Lookup` = `InteractiveStaffOrServiceScopeRequirement(hr-core.lookup)`, using PLAN-50's `IsInteractiveStaff` and
  `Authorization:NonStaffRoles` (one definition). HR.Core's static files: `Gateway.Authenticated`.
- The gateway's `IsMutating` now includes PATCH (HR.Core `EmployeesApi.Patch` met only `Module.HrCore` at the gateway).
- Tests: `GatewayEssRoutingTests` 61 (the plan's table, look-alikes, PLAN-50's approval paths under `/ui/ess`, static files, the lookup
  policy with Keycloak's roles, services, non-staff roles and configuration); updated `ServiceToServiceNegativeMatrixTests` (lookup needs
  staff + tenant; employee and marker refused) and `Hcm028AuthorizationTests` (static files). Gateway/policy filter: 601/601.
- 0.6 decided no page change: no employee or marker page calls the lookup API.

Decision 5. **`/ui/ess` with no controller** (and `/ui/ess/`) answers 404 too: the user guide names `/ui/ess/EssPortal`, which passes.

## P3 summary (Finished (UTC): 2026-11-12T00:18:21Z)

- `Gateway.Audit` (`ConfiguredRolesRequirement` read from `Gateway:Audit:Roles`, default platform-admin, auditor, compliance) on
  `Home/Operations`, `Home/Audit` and `/ops/audit-search`; the menu links and the home page's operations button show only to its holders.
- `GatewayAuditScope` (platform-admin: all rows, including rows with no agency; anyone else: the rows of his valid `tenant_id`; no valid
  tenant: nothing), applied inside `GatewayAuditQueryService.SearchAsync`.
- `ConfiguredRolesAuthorization` (one helper for D-51-2, D-IAM-4 and D-IAM-7): configured roles or defaults, never a service account,
  `hr-admin` dropped for a policy in `Authorization:AdminExcludedPolicies` unless `ProductionConfigurationGuardOptions.IsDemoMode` (the
  guard's own demo checks now use the same method).
- Tests: `Iam051GatewayAuditScopeTests` 12 (in-memory and SQLite with a test-only DateTimeOffset converter; the scope from the caller;
  the policy and its configuration; the three pages carry it); `Iam051GatewayAuditPostgreSqlTests` 2/2 on Testcontainers (three agencies,
  rows with no agency, migrated schema); `GatewayAuditQueryServiceTests` pass the platform scope.

## P4 summary (Finished (UTC): 2026-11-12T00:46:32Z)

- **The filter** (`HrModuleAuthorizationFilter(module, write)`): no name list; override or module policy; then the module's write policy on a
  POST/PUT/PATCH/DELETE that names no policy of its own. `AddHrModuleAuthorizeFilter(module, write)` in the 13 `Program.cs` that use the
  filter (File.Storage has none: decision 8).
- **Overrides** on the 0.7 controllers (class policy they already carry) and on `WorkflowExternalInstances` (`Workflow.ExternalStart`).
- **Policies**: `{Module}.Write`/`{Module}.Approve` for every module through `RolesPolicy` (today's roles by default; HR.Contracts' write
  = admin, hr-contracts); `HrContracts.Approve`, `HrMovements.Write`, `HrHealth.Clinical`, `Payroll.Post`, `Payroll.Reverse` moved onto
  `RolesPolicy` (D-IAM-4 applies); `HrCore.Decision` (decision 3); `Compliance.Dsar` and `Platform.Operator` (decision 4);
  `HrCore.BranchOperation` deleted (no user; `wf043-validate` now refuses it).
- **Writes**: 307 action attributes by a Roslyn tool (scratchpad `policytool`; every row placed exactly once), the five HR.Core decisions
  on `HrCore.Decision`, roles-only writes named, `Culture.SetCulture` on `Gateway.Authenticated`, HR.Contracts' controllers name
  `Module.HrContracts` for reads, interventions and analytics reconcile on their write policy. Sensitive: 36 on `{Module}.Approve` (+ the
  five HR.Core decisions); HR.Core `Signatures.Verify`/`ExportJobs.Cancel` on `HrCore.ReadOnly`; other services' `ExportJobs.Cancel` on the
  module policy.
- **HR.Movements**: the twelve entity bindings replaced by view models (`Models/MovementForms.cs`: only the fields each form posts, the
  entity's validation, `ToEntity()` for a failed form).
- **D-IAM-4**: `Authorization:AdminExcludedPolicies` in HR.Health and Payroll Production and Staging (HR.Health's Staging file is new) and
  the prod-like mirror; off when `Deployment:Mode=demo`.
- **Lists**: write lists only `payroll.txt` (16, PLAN-53); sensitive list gone (0); entity lists without HR.Movements (113 IAM-F rows in 9
  services); `CeilingList.Owners` = PLAN-53, IAM-F; ceilings lowered. HR.Core's old list and `HrCorePostActionsHaveExplicitPolicyTests`
  deleted (`hcm025-validate` refuses their return and checks PLAN-25's rule in the new test).
- **Tests**: `Iam051ModuleFilterAndPoliciesTests` (filter, D-51-2, D-IAM-4 from the real Production/Staging/Development files and the
  prod-like compose, demo mode); `Iam051DirectServiceWriteTests` (in-process HR.Core: the 40 former writes 403 for
  hr-employee+hr-core-supervisory and pass for hr-core, the 2 reads on ReadOnly; every `api/internal` route 403 for a user holding every
  role). Adapted (P-9): Hcm028/Hcm031/Wf045 filter tests use the real controller attributes; Sec001 checks the named policy;
  FineGrainedPolicyTests, Wf043/Wf045 policy lists, GatewayPath branch-manager test on `Workflow.StepAction`.
- Results: unit 6262/6262; architecture 128/128; integration without Testcontainers 371 passed, 4 skipped; iam050, wf043, hcm025, pol047,
  pol049, int037 validate PASS; full `dotnet format` on the changed files clean (37 s).

Decisions:
6. **`Iam050MovementApprovalPolicyTests.The_override_never_sits_on_a_write_action`** forbade a class-level override in five assemblies;
   P-6 requires class-level overrides on the controllers of the name list. The movement controllers still may not carry one (unchanged);
   elsewhere the test now requires every write under a class override to name its own policy. Deviation from P-9 ("unchanged"), recorded.
7. **`hcm025-validate` failed on `origin/main`** (PLAN-50 renamed `GetBlockReasonAsync` to `GetDecisionAsync`): the check accepts both.
8. **File.Storage** never had the module filter (its writes all name policies; S2S and user routes mix), so it gets no write default; its
   list is empty and pinned at 0.
9. **Writes that named only a read policy** (found by a catalog pass): HR.Contracts and HR.Movements `ProcessInterventions.Cancel/Resume`,
   Dev.Analytics `Reconcile` and `InjectDrift` now name their write policy; the rest are reads (exports, lookups, evaluate, verify,
   cancel own export, self-service, culture).

## P5 summary (Finished (UTC): 2026-11-12T01:07:30Z)

- `HrCore.Write` = `RolesPolicy(admin, hr-core)`: no service branch. `hr-core.workflow` is accepted on `Workflow.ExternalStart`
  (start, cancel, external status), `Service.CodeGenerate` (generate, preview) and `Service.PolicyEnforce`, besides the reads §5 rule 1
  keeps (`Module.HrCore`, `HrCore.ReadOnly`, `Workflow.DefinitionRead` unchanged). `FineGrainedPolicyTests` inverted.
- Onboarding: `Platform.AgencyOnboarding` (platform-admin) on the controller, its override and PLAN-50's gateway table; `apply-file` and
  its row deleted; `AgencyManifestLoader` stays. `iam050-validate` rows, `GatewayApproverReachTests` (onboarding left the approvals) and
  `GatewayPathAuthorizationMiddlewareTests` updated.
- Evidence: `Service.EvidenceIngest` = `ConfiguredServiceClientsRequirement(evidence.ingest, Evidence:IngestClients)`, empty by default
  (gateway `appsettings.json` documents the empty list). No caller exists (0.5).
- Wiring matrix: no entry used `HrCore.Write`/`BranchOperation`; unchanged; `ProductionWiringMatrixTests` green.
- Tests: `Iam051OnboardingEvidenceTests` (5); `Iam051DirectServiceWriteTests` grew to 34: every HR.Core call of inventory 0.5 (table A)
  as its own client and full scope set passes its policy (the code kind and policy domain each client owns in the body); a service with
  `hr-core.workflow` reaches no HR.Core write beyond the three endpoints, the other scopes' internal routes and the POST reads; it cannot
  create an employee (count unchanged).
- Results: unit 6267/6267; architecture 128/128; iam050 (+ self-test 8/8), wf043, hcm025, int037 PASS.

## P6 (in progress)

- Acceptance pieces written (uncommitted until the P6 commit): `AcIam51ExplicitPolicyTests` (51a..f + a catalog fact), personas
  `a-ess-supervisory` (HQ 46) and `a-ess-approver` (HQ 47), `AcceptancePersonaCatalog` AGENCY-A 44 -> 46, `acceptance-verify.ps1`
  DATA-AUTH 65 -> 67 and DATA-DIRECTORY 56 -> 58, `ci-scope.json` (prodLike main, required), `ac-ownership.json` (AC-IAM-51, PLAN-51),
  `scripts/iam051-validate.ps1` (PASS; -SelfTest 9/9). Catalog facts 28 passed + 1 skipped; scope/ownership unit guards 151/151.
- Run 1 started 2026-11-12T01:19:34Z: acceptance-reset + `acceptance-job.ps1 -Profile ProdLike -Classes <P-11 prod-like list + s2s42 + pol49
  overlay classes>` (log `artifacts/plan51/acceptance-run1.log`). C: 22.5 GB free; no test port in the excluded ranges.
- Independent review launched at the same time (read-only agent; findings to `artifacts/plan51/review.md`).

### Run 1 failures (all in AC-IAM-51) and their causes

- **51a, 405 instead of 404** on `POST /ui/ess/Shifts/Create`: the gateway answered 404 and recorded `Denied:EssNotExposed`, then
  `UseStatusCodePagesWithReExecute("/Home/ErrorStatus/{0}")` re-executed the empty 404 with POST against a GET-only page (405). Fix: the
  ess 404 carries a body (text/plain), so the status pages leave it alone; unit test checks the content type.
- **51c, no agency on the audit rows**: `GatewayAuditQueue.CloneEntry` (the copy written to the database, since the initial commit)
  dropped `TenantId`, `ScopeOrgUnitId`, `Reason`, `ResourceType`, `ResourceId`; every persisted row had no agency, so the new tenant filter
  would have shown an agency nothing. Fix: the clone copies them (the hash chain already covered them); `AuditDurabilityTests` proves it.
- **51e, 400 "the requester cannot be determined"**: the test's start lacked `X-Acting-User` (Admin.Logistics sends it). Fix in the test.

### Independent review (one fresh read-only agent, `artifacts/plan51/review.md`): no high, 2 medium, 7 low

| # | Finding | Action |
|---|---------|--------|
| M1 | Any policy counted as a write's "explicit" policy, so a write under a class read policy (`Module.HrContracts`, `HrCore.ReadOnly`) would pass the guard and let a reader write in the service | New rule in `WriteActionsHaveExplicitPolicyTests`: a write whose policies are all read policies must be a declared POST read (22, each with its reason, pinned); `GuardSelfTests` plants one. File.Storage upload/delete name a new `FileStorage.Write` (the module's roles, unchanged) |
| M2 | `/ops/metrics-summary` had no policy and, with the Operations page, showed every agency's recent error paths | `Gateway.Audit` on it; the recent error paths for `platform-admin` only (`MetricsFor`) |
| L1 | `/ui/ess` fell back to `Module.Ess` if configuration dropped the `ess` entry | the ess segment is always decided (no entry = no controller); unit test |
| L2 | Actions without a verb attribute are never "writes" in the guard | the filter applies the write policy by the request's method, so a POST to such an action meets it at run time (`Iam051ModuleFilterAndPoliciesTests`); Payroll's `PayrollReports.Payslip` (a GET that marks a payslip sent) recorded for PLAN-53 |
| L3 | The entity-binding guard saw direct entity parameters only | it now sees arrays, collections and a bound model's entity properties (one level); `GuardSelfTests` plants both |
| L4 | `Iam050MovementApprovalPolicyTests` relaxed | recorded as deviation (decision 6) |
| L5 | Sensitive names outside the plan's exact list (`ApprovedCVs.FinalizeApproval`, `ELearningCourses.TogglePublish`, `SelectionCommittees.UpsertDecision`, `*.RestartApproval`) stayed on the write policy | `TogglePublish` -> `HrTraining.Approve` (same roles). `FinalizeApproval` is one of the 42 the plan gives `HrCore.Write` explicitly (§4), so it keeps it; `UpsertDecision` and `RestartApproval` are HR workflow steps, not approvals: left for PLAN-65's review |
| L6 | Payroll's run page showed hr-admin the Post/Reverse buttons where D-IAM-4 refuses them | the page asks the two policies |
| L7 | HR.Contracts `HomeController` named no read policy | `Module.HrContracts`, named |

### Acceptance runs (local, P-11)

| Run | What | Time (UTC) | Result |
|-----|------|-----------|--------|
| 1 | reset + build + prod-like journey, main (AC-IAM-51, AC-IAM-50, AC-HCM-25, AC-WF-43, AC-TEN-48, AC-POL-49), isolated (AC-INT-37, AC-MSG-40, AC-WF-45, AC-WF-46, AC-POL-47), pol49 and s2s42 overlays | 01:18:55 - 01:57:19 (38 min; stack 261 s on its restart) | everything green except AC-IAM-51a, c, e (causes above) |
| 2 | rebuild (review fixes) + AC-IAM-51, AC-INT-37 | 02:06 - 02:28:50 | AC-IAM-51 a, d, e, f and the catalog fact pass; 51b, 51c, and AC-INT-37c ended in "connection forcibly closed" at 02:25-02:26 while Docker's backend log shows a burst of another project's container starts (memory: other-project churn); AC-INT-37b/e blocked on data other classes create (the journey's appointment, AC-IAM-50a's law50 employee) |
| 3 | -NoBuild, fresh stack: journey, AC-IAM-50, AC-IAM-51, AC-INT-37 | 02:30 - 02:39:17 | journey 1/1, AC-IAM-50 5/5, AC-INT-37 8/8, AC-IAM-51 6/7: 51c refused users get the gateway's own refusal, 302 to `/Account/AccessDenied` (cookie scheme), not 403; the test accepts 403 or that redirect, nothing else |
| 4 | -NoBuild, fresh stack: AC-IAM-51 alone | 02:39 - 02:44:43 | **AC-IAM-51a..f + catalog fact 7/7, `acceptance-job: PASS`**; evidence copied to `artifacts/plan51/evidence-run4/` |
| - | seeded counts by psql on run 4's stack (32 of acceptance-verify.ps1) | 02:46 | 31 equal (DATA-AUTH 67, DATA-DIRECTORY 58); DATA-ASSET 451 vs 450 = the asset AC-IAM-51e inserts as setup (counted after the run) |
| 5 | -NoBuild, stage prodLike:leastpriv (AC-OPS-38, AC-TEN-48 least privilege) | 02:47 - 02:50:06 (stack 65 s, tests 204 s) | 9/9, `acceptance-job: PASS` |
| 6 | -NoBuild, lab: Ac05, Ac07, AcEst24, AcHcm21, AcHcm22, AcHcm28, AcHcm31, AcWf23, the lab journey, AcHcm26, AcHcm27 | 02:51 - 03:08:20 | main 27/27, journey 1/1, AcHcm26 5/5, AcHcm27 4/4, `acceptance-job: PASS` |

The stack was brought down with `acceptance-reset.ps1` (volumes deleted) at the end of P6; the committed `acceptance-evidence/` was never touched (the job writes under `artifacts/acceptance/`).

## P8 BLOCKED (2026-11-12T03:20:19Z)

- Before P8: the full Testcontainers step on the host with CI's filter (Release): **216 passed, 1 skipped
  (`Ops038BootstrapIdempotencyTests`, as in PLAN-49/50), 0 failed** (9 min with the Release build, 4 min 22 s of tests). P9's docs written
  so far are committed with this checkpoint.
- **SSH to hrm-vps still times out** (`connect to host ... port 22: Connection timed out`), as at every check since P0 (decision 1); TCP 22 to
  github.com times out too, while the server answers ping and 443. P8 (backup, server build, deploy, server checks) cannot run from here.
- Stood still from here: waiting for the owner (cause: outbound SSH blocked on this machine's network).

## Next step

When `ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm` again: P8 from its step 1 (P-14's condition on the running tag, then the
backup and its verification, the read-only before-picture of P-13, one detached build of HEAD, `scripts/vps/deploy.ps1 -Tag <sha12>`,
`AcIam51VpsChecks` and the PLAN-36..50 server suites), then P9 (OPS_VPS_SINGLE_HOST, the plan's status and notes, the final report,
ci-local and the pre-push check on the final HEAD, the release branch, the PR and its merge).

## Stopped (2026-11-12T03:23:13Z)

- BLOCKED: P8 needs SSH to hrm-vps; outbound TCP 22 times out from this machine (rechecked at 2026-11-12T03:23:13Z, to the server and to github.com).
  The demo server was not touched by PLAN-51 and still answers (gateway and sign-in pages 302 at 2026-11-12T03:23:13Z).
- Keep-awake stopped at 2026-11-12T03:23:13Z (nothing runs while waiting); start it again before P8.
- Question to the owner, with options:
  (a) restore outbound SSH from this machine (disconnect the VPN, or allow port 22) and resume: P8 then P9 as planned;
  (b) authorize P9 now (release branch, PR, merge) with P8 as a follow-up deploy once SSH works;
  (c) run P8 from another machine that reaches the server, then resume P9 here.

## Resumed 2026-11-12T05:17:18Z (owner: "SSH fixed, resume"), stopped again 2026-11-12T05:25:57Z

- `ssh hrm-vps whoami` timed out at 05:17Z and at 9 retries 05:20Z..05:25Z (Git's and Windows' OpenSSH alike). From this machine now:
  TCP 22 to the server False, 443 True; the control port 23456 False, so no VPN answers every port any more and the probes are true;
  github.com:22 times out while ssh.github.com:443 connects. No Windows Firewall outbound block (default outbound not configured, no
  blocking rule on 22); the route leaves through the Wi-Fi gateway and the provider. So port 22 is filtered upstream of this machine
  (router or provider), not on the server and not by the VPN.
- `origin/main` is still PLAN-50's merge (0db7292e); no source changed after 9d130248, so P8 will build HEAD.
- BLOCKED again at P8. Options: (a) let this machine reach port 22 (router or provider setting, a phone hotspot, or another network);
  (b) P9 now, P8 later; (c) P8 from another machine.
