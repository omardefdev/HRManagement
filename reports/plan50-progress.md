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
| P7 | done (this commit) | 2026-11-11T19:03:22Z |
| P8 | done: PR 48 merged at `0db7292e` | 2026-11-11T22:55:32Z |

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

## P8 — docs, local gates, report, PR

- Docs (one commit with this report): `docs/SOD_MATRIX.md` (the delegation table, its roles and limits, the `sub` match of old rows),
  `docs/WF_001_WORKFLOW_ENGINE.md` and `docs/WF_002_WORK_INBOX.md` (the real effect), `docs/HIRE_RBAC_MATRIX.md` (the approver's
  reach; the approvals check the employee's scope and cadre), `docs/USER_GUIDE_AR.md` (section 7, the delegation page),
  `docs/OPS_VPS_SINGLE_HOST.md` (the PLAN-50 section: the new demo user, the backup, the change-table rows, the rollback note, the
  deploy), the «الحالة» lines of G-IAM-01, G-MOV-01 and G-WF-11 («جزئي — الأثر في المحرك والحركات»), the plan's Status and
  «ملاحظات التنفيذ والانحرافات». `reports/plan50-final-report.md`, `reports/plan50-pr.md`.
- Local gates: the Testcontainers step (above, «Test results»); ci-local and `plan035-prepush.ps1` on the final HEAD: below.

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

P8 (started while the server waited on DNS): the full Testcontainers step on the host with CI's filter
(`Category=Testcontainers&Category!=ObjectStorage`, Release, `RUN_TESTCONTAINERS=true`) on the code of `a504f6e5`
(16:23:45Z..16:31:02Z; tests 4 min 40 s): **214 passed, 1 skipped** (`Ops038BootstrapIdempotencyTests`, skipped as in PLAN-49),
**0 failed**, of 215.

An early `scripts/ci-local.ps1 -SkipTestcontainers -ContinueOnError` on `a504f6e5` (16:33Z..16:51Z, 1114 s): 88 passed, **1 failed
(Check formatting: 37 WHITESPACE errors in three PLAN-50 test files)**, 2 not-run (the Testcontainers step and its coverage gate, run
on the host above). Fixed with `dotnet format whitespace` on those three files (`defc46e5`, tests only, so the image build of
`a504f6e54921` stands); the format verify passes on the five PLAN-50 test files; the PLAN-50 unit classes 127/127; the acceptance
project builds. ci-local runs again on the final HEAD before the push.

## Server changes and their undo

Read-only before P7 (15:40Z..16:15Z): the running tag `046316bde9e7` is on `origin/main` (`git branch -r --contains`); 22
containers, 20 report healthy (two have no health check); 47 GB free. `ApprovalDelegations` 0 rows, `WorkflowDelegations` 0 rows;
last migrations `POL049_PolicyDomainRetirement` (hr_core_db) and `MSG040_InboxStateKeys` (hr_movements_db); 40 transfers, 20
transfer approvals, 0 promotions; 425 active employees (`artifacts/plan50/vps/before-p7.txt`).

Changes, in the order they were made (P7, 2026-11-11):

1. **The backup** (16:18:06Z): `/opt/hrm/backups/pre-plan50-20261111T161806Z` (700; `pg_dumpall.sql` 29.3 MB, `env.backup`,
   `SHA256SUMS`: 600). Checked: 17 `CREATE DATABASE` statements (`hr_core_db` and `hr_movements_db` among them), the dump's completion
   line, `sha256sum -c` OK (`artifacts/plan50/vps/backup.txt`). Undo: none needed; delete the folder when it is no longer wanted.
2. **The build** of `a504f6e54921` (the P5 commit plus the gitleaks allowlist; the commits after it change no image): source
   archive 32.2 MB uploaded in 235 s, then `build-on-host-a504f6e54921.sh` started at 16:22:35Z under `setsid nohup`, writing
   `/opt/hrm/build-a504f6e54921.done`. **Attempt 1 failed at 16:23:20Z (exit 1, 45 s):** the runtime stage's `apt-get` could not
   resolve `archive.ubuntu.com` / `security.ubuntu.com`. The server's two provider resolvers (`195.179.224.53`, `209.126.15.53`)
   answer SERVFAIL for every `ubuntu.com` name ("No Reachable Authority: delegation ubuntu.com"); `github.com` and
   `mcr.microsoft.com` resolve, and `1.1.1.1` resolves `archive.ubuntu.com`. An upstream DNS fault, not this code; the server's
   resolver configuration is not changed (not this plan's to change). Nothing was pushed or deployed; the demo kept running
   `046316bde9e7`. A local watcher probed the server's resolver every minute (`artifacts/plan50/vps/dns-wait.log`): FAIL from
   16:27:06Z to 16:54:06Z (26 probes), OK at 16:55:11Z, and it started **attempt 2 at 16:55:16Z** with the same archive and
   script (attempt 1's log kept as `build-a504f6e54921.attempt1.log`). Attempt 2 ended at 17:19:19Z: build 1406 s, push 26 s,
   **17 of 17 images** tagged `a504f6e54921` in the loopback registry (`artifacts/plan50/vps/build.txt`); the script removed the
   source, the build cache and the SDK image; 39 GB free after it. Undo: none needed (the previous tag's images stay).
3. **The deploy** (17:20:12Z to 17:23:24Z with the self-test; first attempt): `scripts/vps/deploy.ps1 -Tag a504f6e54921`
   (pull 2 s, up 96, bootstrap 43, Keycloak reconcile 9, healthy 13); `deployments.log` says healthy at 17:22:43Z, previous tag
   `046316bde9e7`. The services were away for about 1 min 50 s (from the start of `up` to healthy). Self-test (smoke with mandatory
   wiring): 50 pass, 0 fail, 1 warning (the client-side port probe, as before); `svc-hr-movements` gets its token with
   `hr-core.lookup`. Start-up applied `HCM050_ApprovalDelegationScope` and `MOV050_ApprovalOnBehalfOf`; `WorkflowDelegations` is gone;
   the migration revoked nothing (0 rows existed). **Undo:** `scripts/vps/deploy.ps1 -Rollback` (no SQL, no restore; the rollback
   note in `OPS_VPS_SINGLE_HOST.md`).
4. **The demo user** `e2e-delegate-hr` (17:24Z): `scripts/vps/add-demo-user.sh --employee-id 98655dae-f87e-4d46-9703-a17c0b5cc09a
   e2e-delegate-hr e2e-delegate-hr@hrm.local DelegateHr 00000000-0000-4000-8000-000000000001 hr-core offline_access` (inside
   `hrm-keycloak`, the demo users' password, never printed). Read back from `keycloak_db`: `employee_id` and `tenant_id` stored, no
   `hr-management-approver`; the live user profile is back as it was (no unmanaged-attribute policy, `employee_id` still undeclared);
   the realm's `hr-gateway-client` already maps `employee_id` into the token. **Undo:** delete the user in the Keycloak console (§7).
5. **The PLAN-50 server checks** (`AcIam50VpsChecks`, 3/3, 5 min 48 s; a first try at 17:25Z never reached the server: the test
   process's ssh was refused at connect right after a burst of ssh calls, and passed unchanged 45 s later): migrations 1/1,
   `WorkflowDelegations` 0, in force without a delegable role 0, revoked by the migration 0, the two checks present; the approver
   opens the five lists (200), is refused on create (403), the creator is refused on approve (403), a transfer approved by the
   approver; `e2e-checker-mgmt` delegates to `e2e-delegate-hr` for one day (TransferRequest), both see it, the delegate opens the
   list at once, approves a transfer on behalf (`TransferApprovals.OnBehalfOfUserId` = the principal), the internal endpoint refuses a
   signed-in user (403), and after the revocation the list answers 403 (the cached answer had already expired, so at once). No
   workflow step on the server names `hr-management-approver` for these users without a seeded instance, so the engine's on-behalf
   path is proved by AC-IAM-50d (acceptance), as P7 allows.
   **What they leave:** two transfers marked «PLAN-50 server check» created by `e2e-movements-ops` and cancelled by him (one approved by
   the approver, one on behalf), one revoked delegation, audit rows.
   *Correction (after the suites):* the server's demo users can bring a requisition to its step 2 (the PLAN-43 check does it), so a
   fourth check was added rather than leaving the engine to the acceptance run (step 7 below).
6. **The server suites** (17:33:24Z to 18:55:26Z, 1 h 22 min, nothing heavy beside them; `artifacts/plan50/vps/suites/`): **39 of 40
   passed**: `AcOps36VpsBootTests` a to k without 36j (excepted as before), `AcInt37VpsServerTests`, the server checks of PLAN-38, 39,
   40, 43, 44, 45, 46, 47, 48, 49 and the three of this plan. By check: OPS-36 10/10 (1021 s), POL-49 3/3 (689 s), MSG-40 3/3 (687 s),
   WF-46 3/4 (469 s), WF-45 3/3 (430 s), IAM-50 3/3 (343 s), POL-47 2/2 (289 s), OPS-39 2/2 (267 s), WF-44 3/3 (223 s), OPS-38 1/1
   (190 s), TEN-48 1/1 (118 s), WF-43 2/2 (86 s), the realm, service and smoke checks of INT-37 3/3. The one failure,
   `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist` ("No cycle of the CardRequest sweep in the log of idcard-web"), read the card
   service's log before its first sweep cycle after AC-OPS-39f recreated the services, as in PLAN-46..49. It took four times
   PLAN-49's 20 min: the checks themselves (sum of test times 82 min) were slow, with the server's load average under 1.1.
7. **Reruns alone** (18:57Z to 19:02Z, 5 min 2 s): AC-WF-46 sweeps **passed**; and the new
   `AC_IAM_50_vps_a_delegate_approves_step_two_of_a_requisition_on_behalf` **passed**: hradmin filed requisition `4696aa88` on a vacant
   funded position; `e2e-delegate-hr` approved step 1 in his own name (hr-core) and step 2 on behalf of `e2e-checker-mgmt` under a one-day
   RecruitmentRequisition delegation: Approved / Completed; step 1's history entry has no `OnBehalfOfId`, step 2's names the principal
   and the delegation; the signature is the delegate's in the role `hr-management-approver`; the delegation is revoked. It leaves that
   approved requisition («PLAN-50 server check») and its revoked delegation.
8. **At the end** (19:02Z): tag `a504f6e54921`, 20 containers healthy (the two others have no health check), 39 GB free, the self-test
   with mandatory wiring 50 pass, 0 fail, 1 warning. The committed evidence the suites rewrote (`acceptance-evidence/`, 41 entries) is
   restored; a copy is in `artifacts/plan50/vps/suites/acceptance-evidence-from-run/`; the AC-MSG-40 replay report the suite wrote into
   `reports/` was moved there too.

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
| 16:23:20Z..16:55:16Z, 2026-11-11 (32 min, server only) | the demo server's provider resolvers answered SERVFAIL for `ubuntu.com`, so the server image build's `apt-get` failed; the local Testcontainers step and an early ci-local ran meanwhile |

Resumed (UTC): 2026-11-11T11:43:25Z — the owner said «اكمل» after the usage limit (stood still about 38 min). `origin/main` still `1ee60c47`.

## Merge (P8)

Merged by agent on owner instruction (UTC): 2026-11-11T22:55:32Z

- **Local gates on the pushed HEAD `8f446d5f`** (the `--no-ff` merge of the feature branch into `release/plan-50` from `main`; the
  unpushed release merge was recreated twice before the push): `scripts/ci-local.ps1 -SkipTestcontainers` ok, 89 passed, 0 failed,
  2 not-run (the Testcontainers step and its coverage gate, allowed by the pre-push defaults; the full Testcontainers step passed on the
  host: 214 passed, 1 skipped, 0 failed), 959 s; `scripts/plan035-prepush.ps1`: gitleaks, actionlint, cilocal, forbidden, nonprod,
  signatures all PASS. Before it: ci-local on `459e629e` and `97accb81` failed **Check formatting** (whitespace in two PLAN-50 test
  files, fixed in `defc46e5` and `8d4fafec`); the pre-push on `97accb81` found the final report quoting the allow-listed Items slot
  literal (gitleaks generic-api-key; an exact path-and-match entry in `9357bb95`); its `nonprod` "exit 143" was my own 590 s
  timeout, not a failure.
- **Push:** `release/plan-50` at 2026-11-11T20:42:55Z. **PR:** https://github.com/omardefdev/HRManagement/pull/48, opened 20:43:47Z.
- **The first run (43097768410) never started:** queued with no jobs from 20:43:49Z, during a GitHub incident affecting Actions
  ("Impact across multiple GitHub products including Actions, Pages, and Copilot products", opened 20:48Z); after the incident left
  the unresolved list it was still queued and could be neither cancelled nor rerun ("already running"). At 21:48Z the PR was closed
  and reopened, which started run **43106318755** on the same HEAD (the stuck run's record stays "queued", with no job and no minutes).
- **Checks of run 43106318755** (21:48:41Z to 22:54:54Z), all **pass**: Gitleaks 50 s, Reuse check 24 s, Dedupe post-merge run 18 s,
  CodeQL 17 min 35 s, E2E 18 min 58 s, CI 46 min 39 s, Acceptance images 22 min 54 s, Acceptance stage (leastpriv) 9 min 55 s,
  (prodlike-policy) 11 min 41 s, (lab-rest) 18 min 56 s, (prodlike) 24 min 27 s, (lab-main) 26 min 37 s, (prodlike-final) 30 min 25 s,
  (meta) 41 min 21 s, Acceptance 1 min 1 s: 15 of 15. The evidence of record, artifact `acceptance-evidence` of run 43106318755:
  **AC-IAM-50a..e Pass** (prodlike).
- **Merge:** `gh pr merge 48 --merge` at 22:55:32Z, merge commit **`0db7292e`**; `git switch main; git pull --ff-only`: `main` =
  `origin/main` = `0db7292e7ac5`. Post-merge run on `main` (43115764181): success in 1 min 4 s (the dedupe of the tested tree).
- **CI wait:** 2 h 12 min 37 s from the push to the merge: 65 min in the stuck queue (the Actions incident), 66 min of the run, the rest
  checking and merging. **Actions minutes:** about 272 job-minutes for the PR run and 1 for the post-merge run.
- **Timing at the merge:** wall-clock 2026-11-11T10:00:59Z to 22:55:32Z, **12 h 54 min**. Stops: the usage limit (38 min), the host's
  sleep (85 min), the server's DNS (32 min, server only), the stuck CI queue (65 min); active time about 9 h 31 min (wall-clock less the
  usage limit, the sleep and the stuck queue). The demo server runs `a504f6e54921`; the commits after it change tests, docs and the
  gitleaks allowlist only, no image. `scripts/plan-timing.ps1 -From 25 -To 50` after the merge (local times, UTC+3; its End for PLAN-50
  is the release merge commit's time, not GitHub's merge):

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
PLAN-50      13 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08  10:09    


Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.
Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan's End.
```
