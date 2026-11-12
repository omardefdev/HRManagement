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

P6: acceptance pieces (AcIam51ExplicitPolicyTests a..f, two personas a-ess-supervisory and a-ess-approver with employees in agency A, realm generator, catalog tests, DATA-AUTH 67, ci-scope prodLike main required, ac-ownership AC-IAM-51, scripts/iam051-validate.ps1 with -SelfTest), then the prod-like stack run and P-11 list, then the independent review.

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
