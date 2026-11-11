Started (UTC): 2026-11-11T10:00:59.4954239Z

# PLAN-50 progress — IAM-A: the management approver reaches his approvals, and delegation has a real effect

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-11T10:16:47Z |
| P1 | done (`f80845f7`) | 2026-11-11T10:27:05Z |
| P2 | done (`7c2ea8fe`) | 2026-11-11T10:42:12Z |
| P3 | done (`2be807fd`) | 2026-11-11T11:49:22Z |
| P4 | done (`78f09d18`) | 2026-11-11T12:11:36Z |
| P5 | done (this commit) | 2026-11-11T16:14:14Z |
| P6 | done, no merge (`origin/main` still `1ee60c47`, already in the branch; checked 15:40Z) | 2026-11-11T16:14:14Z |
| P7 | pending | |
| P8 | pending | |

## P0 — timing, preconditions, baseline, inventories

Finished (UTC): 2026-11-11T10:16:47Z

- Preconditions: main folder, branch `docs/plan-49-merge-note`; `origin/main` = `1ee60c47` (PLAN-49); tree clean but for `global.json`,
  `tmp-vendors.html` and the untracked prompt; `ssh hrm-vps whoami` = `hrm`, the ED25519 host key is the recorded one; `gh` signed in as
  `omardefdev` with `repo` and `workflow`. No other plan session (the `.claude/worktrees` are old, idle).
- Branch `feature/plan-50-iam-approver-reach-delegation` from `origin/main`. PLAN-49's merge note cherry-picked (`a1bb135a`, from
  `2570ba04`), the prompt committed (`148ce2c5`); `docs/plan-49-merge-note` deleted (its one commit is on this branch). It was the only
  merge-note branch left.
- Baseline (`reports/plan50-baseline.md`): build 0/0 (52.7 s); the plan's filter 957/957 (43 s); the reach table; delegation readers;
  0.6; the inventories (two read-only agents, 10:04Z to 10:15Z, about 11 and 12 min, beside the build and the filter).
- Server (read-only): tag `046316bde9e7` on `origin/main`; `ApprovalDelegations` 0 rows, `WorkflowDelegations` 0 rows; the live
  realm's user profile declares neither `employee_id` nor `tenant_id`; `svc-hr-movements` has `hr-core.lookup` (optional scope).

## P1 — the override attribute, the approver's reach in movements

Finished (UTC): 2026-11-11T10:27:05Z. Commit `f80845f7`. Build 0/0. Tests: `Iam050MovementApprovalPolicyTests` (filter with and
without the attribute, the action's attribute wins, boot failure on an unknown policy, 41 actions × 6 roles, the attribute on 16
read/approve actions and never on a write in five web assemblies, the gateway's exact match with 26 look-alike paths refused,
`Gateway.ApprovalReach` with Keycloak's roles, a service account, a missing, invalid or empty tenant, and the configured list), with
the gateway, filter, Wf043/45/46, Hcm028/031, S2s042, wiring and SoD classes: 1002 + 517 passed, 0 failed.

## P2 — the gateway table, the approval-reach paths, employee corrections

Finished (UTC): 2026-11-11T10:42:12Z. Commit `7c2ea8fe`. Build 0/0. Full unit suite (on the P2 tree before the guard pin): 6103 of
6104 (2 min 57 s); the one failure is decision 6, fixed; the guards rerun 131/131. `GatewayApproverReachTests` (7 facts), the gateway
tests updated (every earlier refusal still asserted; 22 new look-alike and table cases), Wf043/Wf045/Hcm031 expectations moved to
`Gateway.ApprovalReach`.

## P3 — the delegation entity, its creation, the lookup

Finished (UTC): 2026-11-11T11:49:22Z. Commit `2be807fd`. Build 0/0. Unit: `Iam050*`, SoD, wiring, S2S, gateway, mass assignment 722/722.
Integration with Testcontainers (`Iam050|SoDPolicy|Hcm025`): 29/30 at the first run (1 min 38 s); the one failure was the test reading a
column the rolled-back migration never added (fixed); `Iam050*` 6/6 again. `has-pending-model-changes`: none.
`scripts/wf001-workflow-validate.ps1`: pass. P-8's route: the proof is `Iam050DelegationPostgreSqlTests` (lookup and endpoint as the
non-owner app role under FORCE, two tenants, no tenant), not a leastpriv class.

## P4 — the effect in the engine and in movements

Finished (UTC): 2026-11-11T12:11:36Z. Commit `78f09d18`. Build 0/0. Full unit suite: 6157/6158 (1 min 27 s); the one failure was
`S2s042GatewayProofV2Tests.One_hundred_proofs…`, the CPU-timing test PLAN-49 recorded as flaky under load (its §10.7); 54/54 alone.
Engine-related unit filter 2408/2408 with `Hcm025PanelServerParityTests` unchanged. Integration with Testcontainers
(`Iam050|Hcm025|Wf043..Wf046|SoDPolicy`): 79/79 (2 min 25 s). `has-pending-model-changes`: none in either service.
New tests: `Iam050EvaluatorDelegationTests` (13), `Iam050MovementApprovalOnBehalfTests` (8).

## P5 — acceptance, the forced-row-security proofs, the review

Finished (UTC): 2026-11-11T16:14:14Z.

- `AC-IAM-50` (`AcIam50ApproverReachDelegationTests`, `[RequiredAcceptance]`) added to `ci-scope.json` (prodLike, main, required) and to
  `ac-ownership.json`; the personas of the scenario carry `AC-IAM-50`. `scripts/iam050-validate.ps1` (ASCII) and its `-SelfTest`
  (8 planted defects) pass. The P-8 proof (lookup and endpoint under forced row security, as a role that is not the owner, two tenants,
  no tenant) is `Iam050DelegationPostgreSqlTests` (P3/P4 integration run, 79/79).
- Prod-like run 1 (`scripts/ci/acceptance-job.ps1`, 12:17:07Z..12:47:27Z, about 30 min with the image build and stack start): journey
  1/1; `prodLike-main` 23/26; the seven isolated classes (AcInt37 wiring 8, AcMsg40 8, AcWf44 5, AcWf45 8, AcWf46 9, AcPol47 1, the
  rest) all green. The three failures (Ac50a, Ac50b, Ac50e) were the test's own query: `ORDER BY "EmployeeNumber"`, a column
  `Employees` does not have (psql exit 1). Fixed to `"CurrentStatus"=0 ORDER BY "NationalId", "Id"` (also in `AcIam50VpsChecks`);
  every other column the class reads was checked against the live schema. Ac50c and Ac50d passed in run 1.
- hr-core and hr-movements images rebuilt with the review fixes (117 s and 76 s, 13:02:32Z..13:06:09Z) and recreated on the running
  stack. Rerun 1 (`AcIam50…`, `Ac06TransferAccessTests`): Ac06, 50b, 50c, 50d passed; 50e and 50a failed because the host slept
  13:12:08Z..14:37:16Z (Windows Kernel-Power 42/107/1): after waking, the Docker VM's clock still lagged, so the movement service's
  cache had not expired when the test's 75 s budget (host clock) was long past; and 50a's promotion failed the regime rule (the seed's
  employment records name no regime; run 1 never reached this step). 50a now sets law50 on the promoted employee's open record by
  psql as setup, as AC-S2S-42 does. Keep-awake requested for the session.
- Rerun 2 (`AcIam50ApproverReachDelegationTests` alone, 14:39Z..14:42Z): 5/5 in 2 min 32 s. Checks: 50a create/execute/creator-role
  approve 403, approved by the approver; 50b reader 403/403, own legal correction 409; 50c three refusals 400; 50d approved on behalf,
  own request refused, refused after revoke; 50e other type 403, granted after 60 s, refused 60 s after the revocation.
- Lab stack (fresh, `acceptance-reset.ps1` then `acceptance-up.ps1 -Profile Lab -NoBuild`, 14:44:43Z..14:50:47Z, 6 min): the 32 seeded
  counts of PLAN-49's list read by psql right after the start: **0 differences** (DATA-AUTH 65, DATA-DIRECTORY 56; `artifacts/plan50/seed-counts-lab.txt`).
  Lab main (Ac06, AcEst24, AcHcm21, AcHcm22, AcHcm28, AcHcm31, AcWf23): 25/25 in 5 min 55 s; AcHcm26 5/5 (2 min 12 s); AcHcm27 4/4
  (4 min 40 s); lab journey 1/1 (18 s).
- Least privilege: `hr-migrate:local` was from 2026-11-10 (before HCM050/MOV050), so it was rebuilt (372 s, memory note on stale
  migrators); then `scripts/ci/acceptance-job.ps1 -Profile ProdLike -NoBuild -Classes AcTen48LeastPrivilegeTests`: stack 77 s,
  1/1, `acceptance-job: PASS`. Both stacks then went down with their volumes. `scripts/iam050-validate.ps1` and `-SelfTest` pass after
  the fixes. Full unit suite 6166/6166 (1 min 25 s).
- Independent review (one agent, before the server phase): no high finding. Fixed with tests: 1, 2, 3, 4, 9 (decisions 16..20).
  Recorded: 5, 6, 7, 8 (open issues). Then, with the owner's multi-agent mode on, a verification workflow of the fixes (two read-only
  lenses, correctness and scope/security, 15:42Z..15:58Z): six lows, no medium or high. Fixed with tests (decision 21): the corrections
  list refuses an employee of the other cadre asked by id (it listed him empty with his name); `EmployeeCorrections.Approve` and
  `EmploymentRecords.Approve`, which the approver reaches since P2, now check the employee's org scope and cadre (before PLAN-50 only
  unscoped central roles reached them); tests for the loan read scope and for the subject rule on secondments, loans and assignments.
  Recorded (decision 22): rows from before PLAN-50 that name the approver role survive the migration as D-50-4 says, with no stored
  principal scope or employee (0 such rows on the server); a principal whose token has no `employee_id` stores none, so the principal
  side of the subject rule and the self rule by employee cannot fire for his delegations (PLAN-62 gives every user `employee_id`).
  After them: Iam050 unit classes 127/127, full unit suite 6170/6170; hr-core image rebuilt (99 s); `scripts/ci/acceptance-job.ps1
  -Profile ProdLike -NoBuild -Classes AcIam50ApproverReachDelegationTests` on a fresh stack (16:05:38Z..16:13:31Z, stack 307 s):
  5/5, `acceptance-job: PASS`; stack down with its volumes. Unit set after the first fixes 179/179; integration `SoD|Iam050|Msg040` 24/24 (+3 Testcontainers skipped
  without the variable; they run in P8's full step).

## Decisions

| # | Decision | Reason |
|---|----------|--------|
| 1 | The prompt's "three Onboarding API actions" are two (`apply`, `apply-file`); the table carries both | the controller has two |
| 2 | `RequestedBy` of a secondment, loan, promotion and assignment is always the caller (was `??=`, which kept a posted value) | "nobody approves a request he created" would otherwise rest on a client-supplied field; transfers already did this |
| 3 | The gateway opens the movement approval screens on `/ui/hr-movements/...` only, by verb (Index, Details GET; Approve, Reject POST), with at most one GUID id; the module root (the default route, `TransferRequests/Index`) is one of them | P-3/P-4: exact segments; the `/api/hr-movements/...` form of the same MVC actions keeps `Module.HrMovements` |
| 4 | The module filter now passes the `HttpContext` as the resource of its policy check | so `RoleOrDelegationRequirement` can read the action's `[ApprovalEntityType]`, as it does for an `[Authorize]` attribute; no existing module policy reads the resource |
| 5 | Table rows and the workflow paths take an optional id only when it is a GUID; the rules moved in (PLAN-44 `Approvals`, PLAN-47 publish, Onboarding) are therefore narrower or equal (`Approvals/Approve/7` and `/api/onboarding/agencies/other` now get `HrCore.Write`); API rows accept the `/api/hr-core` and `/ui/hr-core` prefixes, as the old `Contains` did | P-4: "nothing wider"; every refusal the old rules proved still holds |
| 6 | `MassAssignmentArchitectureTests` pins twelve older entity bindings of HR.Movements.Web | the scan always probed that assembly but skipped it because the test project did not reference it; PLAN-50 adds the reference, so the debt became visible, not new; replacing them with view models goes with PLAN-51's movement write policies (defect for the final report) |
| 7 | `GatewayApproverReachTests`: where the gateway admits every staff user (`Gateway.ApprovalReach`, D-50-3), a supervisory reader's refusal on a POST is asserted in the service's explicit policy, not at the gateway | the plan's sentence predates D-50-3; the gateway refuses it on every table row |
| 9 | The check constraint uses `trim`, not `btrim` | the in-process HR.Core hosts run SQLite (`EnsureCreated`), which has no `btrim` |
| 10 | The evaluator asks its lookup itself (optional constructor parameter) when the actor's delegations are not loaded | every caller (the action service, the panels, the controllers) gets the same decision without each controller building the actor differently; manual constructions without a lookup stay role-only |
| 11 | The distinct-approver rules (`WorkflowSodRules.DistinctApprover`, the offer override rule) count an approval on behalf for the delegate and for the principal | otherwise a principal could approve a later level of an instance whose earlier level his delegate approved for him |
| 12 | `appsettings.json` carries the `Delegation` and `Authorization` sections; no mirror in `docker-compose.acceptance-prodlike.yml` | appsettings.json loads in every environment, including the prod-like stack's `Acceptance` (the plan asked for a mirror, which only Production-file values need) |
| 13 | `[ApprovalEntityType]` sits on the five movement controllers (class level), not only on Approve/Reject | the list and the details are approval screens too (D-50-3): a delegate scoped to another type reaches none of them |
| 14 | In the inbox's bulk check, the type-specific extra rule (committee membership) runs for the principal with the delegate's employee id | the delegate type `AdditionalBlockReason` has no employee parameter; the server's gate runs the principal pass exactly, so at worst the inbox lists an item the server then refuses with its message (defect for later) |
| 15 | The movements' Reject (transfers) applies the creator-or-principal rule only on behalf | a direct reject had no creator check before PLAN-50; the plan changes no direct rule |
| 16 | Review 1: a reader of transfers and loans admitted through a delegation (`HttpContext.Items`, left only by the delegation path) lists and opens only what lies inside the principal's stored branch, as well as his own scope | the approve path already checked the principal's scope; the read path did not, so a central delegate of a branch principal read every branch |
| 17 | Review 2: the period ends after the creation and within 90 days of it (`DelegationRules.CreatedAt`, passed by the Delegations page) | a 30-day window set 80 days ahead passed the 90-day span rule; chained, such windows covered far more than 90 days |
| 18 | Review 3: on behalf, a movement of the delegate's or the principal's employee is not approved or rejected («لا يجوز للمفوَّض اعتماد إجراء يخصه أو يخص أصيله»), in the five Approve actions and the transfer Reject | the engine's subject rule ran for both; the movements had none; a direct approver's own movement stays as before (no direct rule changes, decision 15) |
| 19 | Review 4: without the acting context (manual constructions), only a holder of a direct role approves | the fallback admitted any caller; production always registers the context, so this is latent only |
| 20 | Review 9: the corrections list applies the cadre restriction of the employees list | the class reads under `HrCore.ReadOnly` since P2, which a cadre-restricted reader passes |
| 21 | Verification of the fixes: the corrections list refuses another cadre's employee asked by id; `EmployeeCorrections.Approve` and `EmploymentRecords.Approve` check the employee's org scope and cadre | P2 opened both to `hr-management-approver`, who may be branch-scoped (P-5 stores such a principal's scope) or cadre-restricted; before, `HrCore.Write`/the gateway kept them to unscoped central roles |
| 22 | Not changed: old approver-role rows keep no stored scope or employee; a principal without `employee_id` stores none | D-50-4 says which old rows are revoked, D-50-2 matches old rows by `sub`; refusing a principal without `employee_id` would stop every demo user (none has it) until PLAN-62; the server holds no old row |
| 8 | The reconcile step does not own the live realm's user profile; it is a precondition of PLAN-62 | `reconcile.sh` runs in the Keycloak image with no jq/awk and reads a flat TSV plan; editing the nested profile JSON with sed could break sign-in for every user. The server's new demo user gets `employee_id` through `add-demo-user.sh` (it opens the profile for one admin write, as for `tenant_id`) |

## Files changed

P0: `reports/plan50-progress.md`, `reports/plan50-baseline.md`.

## Test results

P0: build 0/0; unit filter 957/957.

## Server changes and their undo

None yet (P0 was read-only).

## Open issues

- The live realm's user profile has no `employee_id`: the provisioning fix has no effect there until the profile declares it (P3, P-5).
- `scripts/wf001-workflow-validate.ps1` expects the `WorkflowDelegations` DbSet and table: it changes in P3. (Done in P3.)
- Review 5 (latent): `Workflow.StepAction` admits a delegate of any delegable role on a step without a role or an exempt step; the
  evaluator still decides on the step role, so nothing is approved, but the action is reached. Wave 2, with other delegable roles.
- Review 6: the external outcome events (WF-046) name the delegate, not the principal; the step history and the signature carry both.
  Defect for the events' contract (wave 2).
- Review 7: a principal or delegate without `employee_id` in the token cannot be matched by employee on his own side; demo tokens of
  the live realm lack it until the profile declares it (PLAN-62, decision 8).
- Review 8: a rollback to the PLAN-49 image keeps the new rows; the old Delegations page fails on rows with no `DelegateId`, and the old
  create path fails the role check. The rollback note (P8 runbook) says so, with the SQL to remove PLAN-50 rows if the old page is needed.
- Decision 14 (inbox committee rule with the delegate's employee id) and decision 6 (twelve pinned bindings, PLAN-51).

## Timing

| What | Start | End | Duration |
|------|-------|-----|----------|
| P0 inventories (two agents, read-only) | 10:04Z | 10:15Z | about 12 min |
| Baseline build | 10:02:59Z | 10:03:51Z | 52.7 s |
| Baseline unit filter | 10:05Z | 10:09:11Z | 43 s of tests (about 4 min with discovery) |
| Prod-like acceptance run 1 (build, stack, 9 test steps) | 12:17:07Z | 12:47:27Z | about 30 min |
| Review fixes, unit set 179 | 12:59Z | 13:03Z | build + 4 s of tests |
| Image rebuild hr-core + hr-movements | 13:02:32Z | 13:06:09Z | 3 min 37 s |
| Acceptance rerun 1 (host slept inside) | 13:10:19Z | 14:37:24Z | 87 min, 85 of them asleep |
| Acceptance rerun 2, AC-IAM-50 | 14:39Z | 14:42Z | 2 min 37 s |
| Lab stack reset + start | 14:44:43Z | 14:50:47Z | 6 min 4 s |
| Lab classes (main, two isolated, journey) | 14:51Z | 15:05Z | about 13 min of tests |
| Migrator rebuild + leastpriv stage | 15:05Z | 15:13Z | 372 s build, 77 s stack, 5 s test |
| Full unit suite | 15:14:00Z | 15:15:38Z | 1 min 38 s |
| Verification workflow (two read-only agents) | 15:42Z | 15:58Z | 15 min 32 s |
| hr-core rebuild + fresh prod-like AC-IAM-50 | 16:02Z | 16:13:31Z | 99 s build, 307 s stack, 155 s tests |

## Stops

| Stop (UTC) | Cause |
|------------|-------|
| about 11:05Z, 2026-11-11 | the usage limit of the owner's plan, in the middle of P3 (uncommitted work in the tree, listed below) |
| 13:12:08Z..14:37:16Z, 2026-11-11 (85 min) | the host went to sleep during acceptance rerun 1 (Kernel-Power 42, woke at 107/1); keep-awake requested after it |

Resumed (UTC): 2026-11-11T11:43:25Z — the owner said «اكمل» after the usage limit (stood still about 38 min). `origin/main` still `1ee60c47`.

## Next step (resume here)

P0..P6 are done; P5 is the last code commit before the server (its sha12 is the tag the server builds). In the working tree after
it: the drafted docs (P8).

P7, in order (record every change and its undo as it is made):
1. `cat /opt/hrm/current-tag` is on `origin/main` (046316bde9e7 was at 15:40Z). Read-only picture: `artifacts/plan50/vps/before-p7.txt`.
2. Backup `pg_dumpall` under `/opt/hrm/backups/pre-plan50-<utc>/` (700/600, `env.backup`, `SHA256SUMS`); verify 17 `CREATE DATABASE`,
   the completion line, `sha256sum -c`.
3. Build once, detached: `git archive` of the P5 commit as `scripts/vps/build-on-host.ps1` does, `scp`, then `setsid nohup bash
   /opt/hrm/build-on-host-<tag>.sh <tag> /opt/hrm/src-<tag>.tar.gz 127.0.0.1:5050/hrmanagement` writing a `.done` file; confirm 17 of 17 in
   the registry.
4. `scripts/vps/deploy.ps1 -Tag <sha12>` (rerun at once if keycloak-reconcile fails after PostgreSQL restarts).
5. `ssh hrm-vps "docker exec -i hrm-keycloak bash -s -- --employee-id 98655dae-f87e-4d46-9703-a17c0b5cc09a e2e-delegate-hr
   e2e-delegate-hr@hrm.local DelegateHr 00000000-0000-4000-8000-000000000001 hr-core offline_access" < scripts/vps/add-demo-user.sh`.
6. Server suites: `AcIam50VpsChecks` (in `AcOps36VpsBootTests`), smoke `-RequireIntegrationWiring`, AcOps36 (36j excepted),
   AcInt37VpsServerTests, PLAN-38..49 checks; AC-WF-46 sweeps alone after a sweep cycle; restore `acceptance-evidence/` and `reports/`.
Then P8 (docs, Testcontainers step, ci-local, prepush, final report, release branch, PR, CI, merge, merge note).
