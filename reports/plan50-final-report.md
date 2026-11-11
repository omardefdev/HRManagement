# PLAN-50 final report — the management approver reaches his approvals, and delegation has a real effect (IAM-A)

Plan: [PLAN-50](../docs/acceptance-plans/PLAN-50-iam-approver-reach-delegation.md). Prompt:
[PLAN-50-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-50-AGENT-PROMPT.md). Progress, decisions and timing in full:
[plan50-progress.md](plan50-progress.md). Baseline and inventories: [plan50-baseline.md](plan50-baseline.md).

## 1. Summary per phase

| Phase | What it did |
|-------|-------------|
| P0 | Baseline (build 0/0, unit filter 957/957), the approval reach table, the delegation readers, what PLAN-37..49 changed in the four shared files, and the server read-only: 0 delegation rows, the live realm's user profile without `employee_id`. |
| P1 | `[ModulePolicyOverride]` lets an action name its own policy instead of its module's (an unknown policy stops the start-up). The five movement types: Index, Details and Approve (and the transfer Reject) under `HrMovements.ApprovalAccess`; every other action `HrMovements.Write`. The approver reads (pending by default) and approves; `hr-movements` creates, executes and cancels, and never approves. The creator of a request is always the caller. |
| P2 | One table of HR.Core's gateway exceptions (`GatewayHrCoreActionPolicies`) replaces the `Contains` rules; the named approval paths of both services admit any staff user at the gateway (`Gateway.ApprovalReach`) and the service decides. `GatewayApproverReachTests` keeps every approval action reachable and nothing else open. `EmployeeCorrections`: the class reads, the requests write, the approvals carry `HrCore.Approve` alone. |
| P3 | One delegation entity (`ApprovalDelegation`; `WorkflowDelegation` and its table removed), with a mandatory delegable role, a chosen employee, closed lists of roles and entity types, a stored principal scope, 90 days; the migration revokes the old rows without a delegable role (`HCM050_ApprovalDelegationScope`, under forced row security). A lookup for services (`api/internal/approval-delegations/active`, `Service.ApprovalDelegationRead`, `svc-hr-movements` only), cached 60 s and fail-closed in HR.Movements. Auth.Directory provisions `employee_id`. |
| P4 | The effect: the workflow evaluator accepts a delegation of the step role and type, and runs every identity rule twice (as the delegate, then as the principal with his stored scope); the step history, the signature (14 fields on behalf) and the inbox carry «بالنيابة عن». The five movement approvals and the transfer reject act on behalf, record it (`TransferApproval`, the checklist's approval step; `MOV050_ApprovalOnBehalfOf`), and refuse what the delegate or his principal created. |
| P5 | AC-IAM-50a..e on the prod-like stack, the lab and least-privilege lists, the seeded counts, one independent review and a two-lens verification of its fixes; fixes with tests. |
| P6 | `origin/main` did not move (`1ee60c47`): no merge. |
| P7 | The demo server (section 8). |
| P8 | Docs, local gates, this report, the PR and its merge (sections 7 and 11). |

## 2. Commits

| Commit | Message |
|--------|---------|
| `0d84594d` | chore(iam050): baseline, the approval reach table, delegation readers, what PLAN-37..49 changed, the server's delegations, users and realm profile |
| `f80845f7` | feat(iam050): an action can name its own policy instead of the module's; the approver reads and approves the five movement types, and the creator role does not approve |
| `7c2ea8fe` | feat(iam050): one table of HR.Core's gateway exceptions; the approver reaches every approval action and the delegation page; the named approval paths admit any staff user and the service decides |
| `2be807fd` | feat(iam050): one delegation entity with a mandatory role scope, a chosen employee and closed lists; old rows revoked; a lookup for services; employee_id for provisioned users |
| `78f09d18` | feat(iam050): a delegate approves the delegated role's step and the movement approvals inside the period and the scope, recorded on behalf of the principal, never past separation of duties |
| `3af92605` | test(iam050): AC-IAM-50a..e pass on the prod-like stack; the lookup and the migration proved under forced row security; review fixes; CI scope and ownership updated |
| `a504f6e5` | chore(iam050): gitleaks allowlist for the Items slot name of the delegation handler (a generic-api-key false positive) |

The P7 and P8 commits, the release merge and the PR merge are listed in [plan50-progress.md](plan50-progress.md).

## 3. Decisions applied and deviations

The prompt's decisions D-50-1..D-50-5 and P-1..P-15 are applied as written. The decisions this run took (22) are in
[plan50-progress.md](plan50-progress.md) («Decisions»); the ones that change what the plan says:

- **Onboarding** has two API actions, not three (decision 1).
- **The creator** of a secondment, loan, promotion and assignment is always the caller (decision 2).
- **The gateway** opens the movement approval screens on `/ui/hr-movements/...` only, by verb, with at most one GUID id; the `/api/`
  form of the same MVC actions keeps `Module.HrMovements` (decision 3). The rules moved into the table are narrower or equal
  (decision 5).
- **`MassAssignmentArchitectureTests`** now scans HR.Movements.Web and pins twelve older entity bindings (decision 6, PLAN-51).
- **A supervisory reader's refusal** on a named approval POST is asserted in the service's policy, not at the gateway, which admits
  every staff user there by D-50-3 (decision 7).
- **The check constraint** uses `trim` (SQLite has no `btrim`) (decision 9).
- **The evaluator asks the lookup itself** when the actor's delegations are not loaded (decision 10); the distinct-approver rules
  count an approval on behalf for both the delegate and the principal (decision 11).
- **No compose mirror** of the `Delegation`/`Authorization` sections: appsettings.json loads in every environment (decision 12).
- **`[ApprovalEntityType]`** sits on the five movement controllers, so the list and the details need the matching type too (decision 13).
- **The transfer Reject** applies the creator rule on behalf only; a direct reject is unchanged (decision 15).
- **The reconcile step does not own the live realm's user profile**; `employee_id` reaches the new demo user through
  `add-demo-user.sh --employee-id` (decision 8; PLAN-62).
- **Review and verification fixes** (decisions 16..21) and what was left as decided (decision 22): section 5 and section 10.
- **Acceptance:** 50a sets `law50` on the promoted employee's open employment record by psql as setup (the seed names no regime),
  as AC-S2S-42 does; P-8 is proved by integration tests, not a least-privilege class; the 32 seeded counts are compared by psql.

## 4. The reach table, before and after

Principal: `a-management-approver` (`hr-management-approver` only, `offline_access`, a `tenant_id`). "Gateway" is the policy the
gateway applies to the path; "Service" is what the service applies.

| # | Action | Before: gateway / approver reaches | After: gateway | After: service | Approver reaches |
|---|--------|------------------------------------|----------------|----------------|------------------|
| 1 | TransferRequests.Approve | Module.HrMovements / no | Gateway.ApprovalReach | HrManagement.Approve (role or delegation, type TransferRequest) | yes |
| 2 | TransferRequests.Reject | Module.HrMovements / no | Gateway.ApprovalReach | HrManagement.Approve | yes |
| 3 | SecondmentRequests.Approve | Module.HrMovements / no | Gateway.ApprovalReach | HrManagement.Approve | yes |
| 4 | LoanAssignmentRequests.Approve | Module.HrMovements / no | Gateway.ApprovalReach | HrManagement.Approve | yes |
| 5 | PromotionRequests.Approve | Module.HrMovements / no | Gateway.ApprovalReach | HrManagement.Approve | yes |
| 6 | DelegationRequests.Approve | Module.HrMovements / no | Gateway.ApprovalReach | HrManagement.Approve | yes |
| 7 | the five types' Index and Details (GET) | Module.HrMovements / no | Gateway.ApprovalReach | HrMovements.ApprovalAccess (Admin, hr-movements, approver, or a delegation of the type) | yes |
| 8 | the five types' Create, Edit, Execute, Cancel, Close, Activate | Module.HrMovements / no | Module.HrMovements | HrMovements.Write | **no** (403) |
| 9 | HireWizard.Step3Approve | HrManagement.Approve / yes | unchanged | unchanged | yes |
| 10 | Onboarding apply, apply-file | HrCore.Approve / yes | HrCore.Approve (table) | HrCore.Approve | yes |
| 11 | Approvals.Approve, Reject | HrCore.Approve / yes | HrCore.Approve (table) | HrCore.Approve | yes |
| 12 | EmploymentRecords.Approve | HrCore.Write / **no** | HrCore.Approve (table) | HrCore.Approve; the employee's org scope and cadre | yes |
| 13 | EmployeeCorrections.Approve, ApproveApi | HrCore.Write / **no** | HrCore.Approve (table) | HrCore.Approve; the employee's org scope and cadre | yes |
| 14 | Competitions.Finalize, Cancel | HrCore.Write / **no** | HrCore.Approve (table) | HrCore.Approve (central only, as before) | yes |
| 15 | Delegations.Create, Revoke (POST) | HrCore.Write / **no** | SoD.ManageDelegations (table) | SoD.ManageDelegations | yes |
| 16 | PolicyTemplates.PublishFromUi, api policies publish | PolicyApprover / yes | PolicyApprover (table) | PolicyApprover | yes |
| 17 | the HR.Core workflow step actions (Requisitions, Offer letters, Committees, Establishment changes, Competitions results, WorkflowApprovals, `api/workflow-instances/{id}/approve|reject`) | BranchOperation / Workflow.StepAction | Gateway.ApprovalReach | Workflow.StepAction (roles or any delegable role's delegation; the evaluator decides on the step role) | yes, where the step names his role |

`hr-movements` (`a-movements`): creates, edits, executes, cancels (row 8); refused on every approve (403, AC-IAM-50a).
`GatewayApproverReachTests` (7 facts) fails if an approval action of HR.Core or HR.Movements has no path the approver reaches, or if the
gateway admits anything beyond the named paths (26 look-alike paths refused).

## 5. Delegation: what a delegate can and cannot do

| A delegate… | Proof |
|-------------|-------|
| approves step 2 of a requisition on behalf of his principal, and the step history and the signature name the principal (`OnBehalfOfId`, `onBehalfOfUserId`, `delegationId`; signer role = the delegated role) | AC-IAM-50d; `Iam050EvaluatorDelegationTests` |
| sees the principal's item in his inbox with «بالنيابة عن» | AC-IAM-50d; `Iam050EvaluatorDelegationTests` |
| approves a transfer on behalf, and `TransferApproval.OnBehalfOfUserId` is the principal | AC-IAM-50e; `Iam050MovementApprovalOnBehalfTests` |
| approves a secondment on behalf, and the checklist's approval step names the principal | `Iam050MovementApprovalOnBehalfTests` |
| cannot approve outside the period, after a revocation (at once in HR.Core; within 60 s in HR.Movements, measured 60 s) | AC-IAM-50d, AC-IAM-50e; `Iam050DelegationMatcherTests` |
| cannot reach another entity type (a RecruitmentRequisition delegation gives 403 on transfers) | AC-IAM-50e; matcher tests |
| cannot approve what he or his principal created | AC-IAM-50d; `Iam050MovementApprovalOnBehalfTests`; evaluator tests |
| cannot approve a movement of his own or his principal's employee | `Iam050MovementApprovalOnBehalfTests` (transfer approve and reject, promotion, secondment, loan, assignment) |
| cannot pass a separation-of-duties rule: every identity rule runs as the delegate and as the principal; distinct approver per level counts both | `Iam050EvaluatorDelegationTests` |
| reads and approves transfers and loans only inside the principal's stored branch | `Iam050MovementApprovalOnBehalfTests` (transfers and loans, list and details; approve) |
| cannot delegate the role again (a delegated role is never in a token, a claim or `IsInRole`) | AC-IAM-50c («لا يمكن تفويض الدور … لأنك لا تحمله مباشرة»); `Iam050DelegationMatcherTests` |
| cannot be given a role the principal does not hold directly, a non-delegable role, a type outside the list, more than 90 days (from its start or from its creation), himself | AC-IAM-50c; `Iam050DelegationMatcherTests` |
| cannot read another tenant's delegations; the lookup refuses interactive users and answers nothing without a tenant | `Iam050DelegationPostgreSqlTests` (non-owner role under FORCE, two tenants, no tenant); `Iam050DelegationInternalApiTests`; the server check (403 for a user) |
| gets nothing when the lookup fails (fail-closed); a holder of the direct role never asks | `Iam050MovementApprovalOnBehalfTests`; `RoleOrDelegationRequirement` tests in `Iam050DelegationMatcherTests` |

## 6. The guards and their own negative tests

| Guard | What it fails on | Its negative proof |
|-------|------------------|--------------------|
| `ModulePolicyOverrideValidator` (start-up) | an override naming a policy that is not registered | `Iam050MovementApprovalPolicyTests` (boot failure with an unknown policy) |
| `Iam050MovementApprovalPolicyTests` | any of the 41 movement actions with a policy other than its row, an override on a write action | the 6-role × 41-action matrix |
| `GatewayApproverReachTests` | an approval action without a reachable path; a path opened beyond the named ones | 26 look-alike paths, ids that are not GUIDs |
| `scripts/iam050-validate.ps1` | gateway rows, movement entity types and Approve attributes, configuration lists, the internal route, one entity, the HCM050 FORCE lift, a BOM | `-SelfTest`: 8 planted defects refused |
| `CK_ApprovalDelegations_ActiveHasRole`, `CK_ApprovalDelegations_HasDelegate` | an active delegation with no role, a row with no delegate | `Iam050DelegationPostgreSqlTests` |
| `ProductionWiringMatrixTests`, `S2s042InternalRoutesAreServiceOnlyTests` | the new internal route or policy missing from the wiring matrix, or reachable by a user | both list the new policy and controller |
| `MassAssignmentArchitectureTests` | a new entity binding in HR.Movements.Web (twelve old ones pinned) | the scan now includes the assembly |
| gitleaks (pinned) | a secret in history or range | one false positive (`ItemKey = "PLAN50.ApprovalDelegation"`) allow-listed by exact path and match, as PLAN-48's `ScenarioKey` |

## 7. Unit, integration and acceptance results

| Run | Result |
|-----|--------|
| Full unit suite (after the last code change) | 6170/6170 (1 min 30 s) |
| PLAN-50 unit classes (`Iam050*`) | 127/127 |
| Integration `SoD|Iam050|Msg040` (no Testcontainers) | 24/24 |
| Integration with Testcontainers in P3/P4 (`Iam050|Hcm025|Wf043..Wf046|SoDPolicy`) | 79/79 |
| The full Testcontainers step on the host, CI's filter (`a504f6e5`) | 214 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, as before), 0 failed |
| Prod-like run 1 (`acceptance-job.ps1 -Profile ProdLike`, the P-11 prod-like list) | journey 1/1; main 23/26 (the three AC-IAM-50 failures were the test's own query of a missing column); the seven isolated classes green |
| AC-IAM-50 after the fixes, fresh prod-like stack (`acceptance-job.ps1 -Classes AcIam50ApproverReachDelegationTests`) | 5/5, `acceptance-job: PASS` |
| Lab list on a fresh lab stack | main 25/25 (Ac06, AcEst24, AcHcm21, AcHcm22, AcHcm28, AcHcm31, AcWf23); AcHcm26 5/5; AcHcm27 4/4; lab journey 1/1 |
| Seeded counts (32, PLAN-49's list) by psql right after the lab start | 0 differences |
| Least privilege (`AcTen48LeastPrivilegeTests`) | 1/1, `acceptance-job: PASS` |
| `scripts/iam050-validate.ps1` and `-SelfTest` | pass; 8 planted defects refused |
| ci-local on `a504f6e5` (early) | 88 passed, 1 failed (Check formatting, whitespace of three test files: fixed in `defc46e5`), 2 not-run (Testcontainers, run on the host) |
| ci-local and pre-push on the final HEAD, and the PR run | [plan50-progress.md](plan50-progress.md) (P8); the PR run's Acceptance artifact is the evidence of record |

## 8. The server

| | Before (2026-11-11T16:15Z) | After (19:02Z) |
|---|---|---|
| Running tag | `046316bde9e7` (on `origin/main`) | `a504f6e54921` (previous `046316bde9e7`) |
| `ApprovalDelegations` / `WorkflowDelegations` | 0 rows / 0 rows | the checks' revoked delegations / table dropped |
| Last migrations | `POL049_PolicyDomainRetirement`, `MSG040_InboxStateKeys` | + `HCM050_ApprovalDelegationScope`, `MOV050_ApprovalOnBehalfOf` (0 rows revoked: none existed) |
| Demo users | `e2e-checker-mgmt` (approver, principal), `e2e-movements-ops` (creator) | + `e2e-delegate-hr` (`hr-core`, `employee_id` `98655dae…`, no approver role) |
| Containers | 20 healthy of 22 | 20 healthy of 22 (two have no health check); self-test 50 pass, 0 fail, 1 warning |

**Changes and their undo** (in order; details and timings in [plan50-progress.md](plan50-progress.md)):
1. Backup `/opt/hrm/backups/pre-plan50-20261111T161806Z` (700, files 600; `pg_dumpall.sql` 29.3 MB, `env.backup`, `SHA256SUMS`;
   17 `CREATE DATABASE`, the completion line, `sha256sum -c` OK). Undo: none; delete when no longer wanted.
2. Build of `a504f6e54921` on the server, detached: attempt 1 failed in 45 s because the provider's two resolvers answered SERVFAIL for
   `ubuntu.com` (the resolver configuration was not changed); a watcher restarted it when they recovered 32 min later: build 1406 s,
   push 26 s, 17 of 17 images. Undo: none.
3. Deploy `scripts/vps/deploy.ps1 -Tag a504f6e54921`, first attempt, healthy at 17:22:43Z (pull 2, up 96, bootstrap 43, reconcile 9,
   healthy 13; services away about 1 min 50 s). Undo: `scripts/vps/deploy.ps1 -Rollback` (the rollback note below).
4. Demo user `e2e-delegate-hr` through `scripts/vps/add-demo-user.sh --employee-id` (the profile opened for one write and restored).
   Undo: delete the user in the Keycloak console.
5. Server checks: `AcIam50VpsChecks` 4/4 (the migrations; the approver's reach and the creator's refusal; a transfer approved on behalf
   and the list refused after the revocation, the internal endpoint refusing a user; a requisition's step 2 approved on behalf with the
   history and the signature naming the principal). The suites: 39/40 (OPS-36 a..k without 36j, INT-37, PLAN-38..49 checks, IAM-50);
   the AC-WF-46 sweeps check, which read the card log before its first cycle after AC-OPS-39f recreated the services (as in PLAN-46..49),
   passed alone. They leave, marked «PLAN-50 server check»: two cancelled transfers (one approved on behalf), one approved requisition,
   two revoked delegations.

**Backup location:** `/opt/hrm/backups/pre-plan50-20261111T161806Z`.

**The rollback note** (in `OPS_VPS_SINGLE_HOST.md`): `deploy.ps1 -Rollback` to `046316bde9e7` needs no SQL and no restore. The older code
ignores the two migration rows and the new columns, and never reads the dropped `WorkflowDelegations`. Its Delegations page assumes a
non-null `DelegateId`, so a row this release created (delegate by employee) breaks that page, and its create path without a role
fails the new check constraint; if that page is needed after a rollback, delete the rows with a null `DelegateId` (after a backup).
The approver loses the reach this release opened.

## 9. Deferred items

- **Other delegable roles** (only `hr-management-approver` is delegable, `Delegation:DelegableRoles`), automatic absence, escalation and
  notification of a delegation: wave 2 (WF-F, G-WF-14).
- **The write policies of the other movement controllers** and the removal of the module name list from the filter: PLAN-51, with the
  twelve pinned entity bindings of HR.Movements.Web replaced by view models.
- **The branch scope of secondments, promotions and assignments** (no org unit on the entity) and «edit, then approve» (G-MOV-11):
  wave 2 and PLAN-52.
- **`employee_id` in the live realm's user profile** and for every demo user: PLAN-62.

## 10. Defects for later plans

| # | Defect | Severity | Where it goes |
|---|--------|----------|---------------|
| 1 | `Workflow.StepAction` admits a delegate of any delegable role on a step with no role or an exempt step; the evaluator still decides on the step role, so nothing is approved, but the action is reached (review 5) | low, latent | wave 2, with other delegable roles |
| 2 | The external outcome events (WF-046) name the delegate, not the principal; the step history and the signature carry both (review 6) | low | the events' contract, wave 2 |
| 3 | A principal or delegate whose token has no `employee_id` cannot be matched by employee on his side; demo tokens lack it (review 7); a principal without it stores no `PrincipalEmployeeId`, so the principal side of the subject rule and the self rule by employee cannot fire for his delegations (decision 22) | low | PLAN-62 |
| 4 | Rows from before PLAN-50 that name the approver role survive the migration (D-50-4) with no stored principal scope or employee, matched by `sub` (D-50-2); none on the server | low | none unless such rows appear; then revoke them |
| 5 | The inbox's bulk check runs the committee rule for the principal with the delegate's employee id (decision 14); the server's gate runs the exact check, so at worst an item is listed and then refused with its message | low | wave 2 |
| 6 | Twelve older entity bindings in HR.Movements.Web pinned in `MassAssignmentArchitectureTests` (decision 6) | medium (debt) | PLAN-51 |
| 7 | A rollback to PLAN-49's image keeps the new rows: the old Delegations page fails on rows with no `DelegateId`, and the old create path fails the role check (review 8; the rollback note, section 8) | low | operations note only |

## 11. Timing

Up to the report commit (19:05Z); the push, the CI wait, the merge and the final `plan-timing.ps1` line go into the merge note
(local branch `docs/plan-50-merge-note`), as in the plans before.

- **Wall-clock so far:** 2026-11-11T10:00:59Z to about 19:05Z, **9 h 04 min**.
- **Stops (about 2 h 35 min in all):** the usage limit of the owner's plan, about 11:05Z to 11:43:25Z (38 min, resumed on «اكمل»); the host
  went to sleep during an acceptance rerun, 13:12:08Z to 14:37:16Z (85 min; keep-awake requested after it); the server build waited on
  the provider's DNS, 16:23:20Z to 16:55:16Z (32 min, server only: the Testcontainers step and an early ci-local ran meanwhile).
- **Active time:** about 6 h 30 min (wall-clock less the first two stops).
- **Builds:** service images for acceptance in run 1 (inside its 30 min); hr-core and hr-movements rebuilt 3 min 37 s; hr-core again
  99 s; the migrator 372 s; the server build 1406 s plus 26 s push (attempt 1: 45 s).
- **Acceptance:** prod-like run 1 about 30 min (12:17Z to 12:47Z); AC-IAM-50 rerun 2 min 37 s; lab stack 6 min and its classes about
  13 min; least privilege 8 min with the migrator; AC-IAM-50 on a fresh prod-like stack 8 min (stack 307 s).
- **Deploy:** 151 s to healthy (17:20:12Z to 17:22:43Z), self-test included 3 min 12 s; services away about 1 min 50 s.
- **Server checks:** the suites 1 h 22 min; the PLAN-50 checks 5 min 48 s; the two reruns 5 min 2 s.
- **Local gates:** the Testcontainers step 7 min 17 s; early ci-local 18 min 34 s.
- **Reviews:** one independent review (before P5's commit) and a verification workflow of two read-only agents, 15 min 32 s.
- **Actions minutes and the CI wait:** in the merge note.
- **`plan-timing.ps1 -From 25 -To 50`** (local times, UTC+3; before the push):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
----    ------- -------          -----------      ---              ------ ----   ---------
PLAN-25      11                  2026-10-29 20:31 2026-10-29 23:21   True 2:50            
PLAN-26      12                  2026-10-21 13:38 2026-10-21 22:49  False 9:10            
PLAN-27      11                  2026-10-21 22:57 2026-10-22 09:43  False 10:45           
PLAN-28      13                  2026-10-22 10:03 2026-10-22 15:03  False 5:00            
PLAN-29      13                  2026-10-22 15:21 2026-10-22 20:36  False 5:15            
PLAN-30      21                  2026-10-23 00:45 2026-10-29 07:45  False 150:59          
PLAN-31      14                  2026-10-29 10:24 2026-10-29 15:03  False 4:38            
PLAN-32       9                  2026-10-29 14:12 2026-10-29 16:31   True 2:19            
PLAN-33      11                  2026-10-29 17:49 2026-10-29 20:18   True 2:28            
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58    
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43    
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31   7:40     
PLAN-37      23 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 12:27   True 10:57  11:03    
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47  12:48    
PLAN-39      13 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 11:05  False 8:52   8:53     
PLAN-40      20 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 11:56  False 48:51  48:55    
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08  27:16    
PLAN-43      14 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 19:03  False 8:42   8:50     
PLAN-44      14 2026-11-06 21:11 2026-11-06 21:20 2026-11-07 16:25  False 19:05  19:14    
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15   6:15     
PLAN-46      14 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 15:06  False 15:31  15:32    
PLAN-47      15 2026-11-08 15:06 2026-11-08 15:06 2026-11-09 15:30  False 24:23  24:24    
PLAN-48      12 2026-11-09 18:27 2026-11-09 18:28 2026-11-10 02:18   True 7:50   7:51     
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58  17:00    
PLAN-50      10 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 22:03  False 9:01   9:02     


Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```

## 12. A readiness note

- **PLAN-51:** `[ModulePolicyOverride]` and its start-up validator are in place and used by the five movement controllers; the module
  name list (`HrModuleAuthorizationFilter`) is still there; the other HR.Movements controllers have no write policy of their own yet,
  and twelve entity bindings wait for view models.
- **PLAN-52:** transfers and loans check the org scope of both units (and, on behalf, the principal's stored branch); secondments,
  promotions and assignments carry no org unit, so their lists and approvals are tenant-wide for whoever reaches them.
- **PLAN-62:** Auth.Directory provisions `employee_id` (`KeycloakUserProvisioningService.EmployeeIdAttribute`); the manifest's profile
  declares it; the live realm's user profile does not, so an admin write of it is dropped there except through
  `add-demo-user.sh --employee-id`, which opens the profile for that one write. Declaring it in the live profile (and giving it to every
  demo user) is PLAN-62's precondition.
