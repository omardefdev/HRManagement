Started (UTC): 2026-11-11T23:26:13.6316983Z

# PLAN-51 progress (checkpoint)

Prompt: docs/acceptance-plans/execution/PLAN-51-AGENT-PROMPT.md. Plan: docs/acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-11T23:45:16Z |
| P1 | done | 2026-11-11T23:55:59Z |

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

P2: self-service at the gateway (GatewayEssOptions, exact segment match, 404 + Denied:EssNotExposed audit), HrCore.Lookup for staff and services, Gateway.Authenticated for HR.Core static files, GatewayEssRoutingTests, GatewayPathAuthorizationMiddlewareTests updated.

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
