# PLAN-52 final report — branch scope on everything a branch manager reaches, and a reflective BOLA test (IAM-D)

Plan: [PLAN-52-iam-branch-scope-bola.md](../docs/acceptance-plans/PLAN-52-iam-branch-scope-bola.md).
Prompt: [PLAN-52-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-52-AGENT-PROMPT.md). Progress and every stamp:
[plan52-progress.md](plan52-progress.md). Baseline (the reach matrix, inventories A–C, the server's picture): [plan52-baseline.md](plan52-baseline.md).

## 1. Summary per phase

| Phase | What it did | Finished (UTC) |
|-------|-------------|----------------|
| P0 | Keep-awake, preconditions (SSH passed then, through the VPN), baseline build 0/0 and tests; the reach matrix (`OrgScopeReach`: the gateway, the module filter, the policies and delegation, for five restricted users), inventories A (235 controllers: stance, entity → unit, ids, kept answers, lists), B (the workflow types), C (the catalogues), the server's read-only picture | 2026-11-12T21:22:10Z |
| P1 | One mechanism: `OrgScopeKeyRegistry` (keys translated to SQL), the four stance attributes, `OrgScopeActionFilter<T>` (order −3000); `OrgScopeRequirement`, its handler and both registrations removed; `iam002-validate.ps1` checks the new mechanism | 2026-11-12T21:22:43Z |
| P2 | BOLA v2: `OrgScopeCoverageTests` by reflection over the fifteen web assemblies and the shared controllers, per-service lists that only shrink, planted defects in `GuardSelfTests`, the key translation test on SQLite and PostgreSQL | 2026-11-12T21:23:01Z |
| P3 | HR.Core: 81 controllers declare a stance (list gone); 32 keys; lists, exports and pick-lists filtered; HCM052 (a unit on Diwan and inspections, the instances' unit backfilled, FORCE lifted and restored); `WorkflowInstanceScopeResolver`; the recruitment follow-ups closed | 2026-11-12T22:04:08Z |
| P4 | HR.Contracts (22 controllers, the hire wizard by `transactionId`, the export jobs record the requester's scope), Admin.Logistics (8), HR.Movements' transfers and loans (both units) and its three employee-only types central; the other actual controllers declare | 2026-11-12T22:24:34Z |
| P5 | A transferred restricted user loses his scope (Auth.Directory, fail-closed, Keycloak when the sync is on); HR.Health's snapshot follows the transfer; BOLA v1 deleted, AC-QA-34d reads v2; every list row signed `IAM-H` | 2026-11-12T22:33:02Z |
| P6 | AC-IAM-52a..e; `iam052-validate.ps1`; P-14's lists on prod-like, lab and leastpriv; the independent review (no high; 3 medium, 4 low; all fixed or recorded) and the two leaks its new page scan found; the full Testcontainers step | 2026-11-13T01:36:07Z |
| P7 | `origin/main` had not moved (`e6a93705`): no merge | 2026-11-13T01:36Z |
| P8 | SSH blocked at its start (01:39Z, `BLOCKED:`); resumed at 06:47Z through ProtonVPN. Verified backup, before-counts, one detached build of `92a69960c826`, deploy on the second attempt (the first stopped locally: Docker Desktop down), HCM052 by the least-privileged migrator, `e2e-branch-manager` added, the four PLAN-52 server checks 4/4, the server suites 48/49 + the WF-46 sweeps rerun alone 1/1, final smoke 50/0/1 | 2026-11-13T08:44:00Z |
| P9 | Docs, this report, local gates, the release branch, the PR and its merge | in the merge note |

## 2. Commits

- `76b0acbb` docs(iam051): merge note for PR 49
- `922bd771` docs(iam052): the PLAN-52 agent prompt
- `2b1b34d6` chore(iam052): baseline, the reach matrix with delegation, per-controller ids and keys and answers, workflow types, catalogues, the server's picture
- `c0ac52b0` feat(iam052): one scope mechanism, a registry of keys with declared stances and one action filter; OrgScopeRequirement removed
- `6c0f8519` test(iam052): reflective scope coverage over the fifteen web assemblies, with per-controller lists that only shrink
- `d351dc4b` feat(iam052): HR.Core, every controller declares its scope; Diwan and inspections get a unit; workflow instances are stamped and filtered; the recruitment follow-ups are closed
- `086a3529` feat(iam052): the hire wizard, contracts and exports, logistics, and a delegate's movement approval screens stay inside the branch
- `019a8ce7` feat(iam052): a transferred restricted user loses his scope (fail-closed), HR.Health's snapshot follows the transfer; BOLA v1 replaced by v2
- `92a69960` test(iam052): AC-IAM-52a..e pass on the prod-like stack, AC-02/03/06/32 green; review fixes; CI scope and ownership updated
- `ba4f1e7a` test(iam052): demo server on branch scope (a branch-manager demo user stays in his branch), PLAN-36..51 server checks still green
- this report's commit: docs(iam052): the registry and the filter, the wizard's scope, a transfer is fail-closed, BOLA v2 in CI, the study's status lines, the plan's status and notes, the final report

## 3. Decisions applied and deviations

**Decisions (D-52-1..5, P-4..P-13) as applied:**
- **D-52-1:** a null unit or a missing snapshot is no key: the record is central, 404 to a restricted user. A null unit is coalesced to `Guid.Empty`, which is never in a scope.
- **D-52-2:** a restricted user's new record without a unit takes his scope's root (`GetRestrictedRootAsync`); Diwan takes the related employee's unit, else the chosen unit, else the root. An edit that would clear the unit keeps the root (key positions, supply orders, the marketplace vacancy) or is refused (a receipt without its order, a contracts decision without its employee).
- **D-52-3:** the inventory, vendor and shift catalogues are written centrally (403 to a restricted user); `JobCatalog`/`ReferenceCatalog` stay central-only; other catalogues are not applicable (reference data) and keep their write policies.
- **D-52-4:** the transfer clears the scope, keeps `branch-manager`; with the sync off the token keeps the old scope until the sync runs (warning and counter).
- **D-52-5 and P-4's two answers:** 404 wherever the plan's scenarios name a screen; the older 403s kept where a test pins them (§5).

**Deviations (none defers part of the plan):**
1. The keys classes live in each service's `Infrastructure/OrgScope` (they need the DbContext); `WhereInOrgScope` takes the DbContext (`WhereInOrgScope(registry, scope, db)`), so a key can be a subquery.
2. The lists are one file per service (`tests/architecture/org-scope-allowlist/<service>.txt`), as PLAN-51's, not one file.
3. `WorkflowApprovals` keeps its own unit check, declared not applicable with its reason: the generic filter would drop PLAN-45's requester exception. The baseline row had said OrgScoped 403.
4. Step decisions on `api/workflow-instances` (approve/reject) keep the workflow gates' answer (AC-WF-43e pins 400); `List`, `Get`, `History` take the 404.
5. The legacy approval queue is scoped (P-12): AcWf23's seed now names a real `ApprovedCV` of EmpBr1; its assertions are unchanged.
6. AC-IAM-52b and 52e write their records as stated test-only preconditions (no short UI path: an appointment comes only from a recruitment hire, an internal vacancy from a requisition published internally; AC-HCM-29 and AC-HCM-26 prove those paths), and delete them when the scenario ends.
7. AC-OPS-38f's wait helper treats a transport error inside the wait as "not yet" (§8); its assertions are unchanged.

## 4. The reach, before and after

Restricted users (D-IAM-3): `bm-core-logistics` (`br1-mgr`, `br2-mgr`: branch-manager, hr-core, admin-logistics), `bm-core` (six branch personas), `bm-only` (`negative-invalid-scope`), and two synthetic users: `bm-delegate` (an employee and a delegation of `hr-management-approver`) and `bm-employee` (also hr-employee). The full per-controller table is the baseline's 0.3 (235 rows).

| Service | Actual controllers | Path that reaches them | Before PLAN-52 | After |
|---------|-------------------:|------------------------|----------------|-------|
| hr-core | 66 (+2 self-service for `bm-employee`) | `Module.HrCore`/`HrCore.ReadOnly` admit branch-manager; `HrCore.Write` admits hr-core | 28 of 72 controller files mention the scope; Diwan, statistics, inspections, attendance writes, rosters, succession, `api/workflow-instances`, the marketplace unscoped | all 81 declare (34 OrgScoped, 13 central only, 26 not applicable, 8 recruitment); list gone |
| hr-contracts | 1 (`HireWizard`, 12 of 14 actions) | `HrContracts.HireView`/`HireExecute` admit hr-core | `Index`/`Continue` scoped; `Step2`..`Step4`, the decisions, the execution by `transactionId` unchecked | all 22 declare; every step by `transactionId` 404; QA-034's 403 kept |
| admin-logistics | 8 | `Module.AdminLogistics`/`AdminLogistics.Write` admit admin-logistics | assets and custody scoped; supply orders, receipts, maintenance, the catalogues' writes open | all 8 declare; orders, receipts, maintenance 404; catalogue writes 403; assets and custody keep 403 |
| hr-movements | 5 | delegation only (`bm-delegate`) | transfers and loans scoped (PLAN-50) | the same, by the filter (both units, 403 kept); the three employee-only types central, also for a delegate of a branch-scoped principal (review 1) |
| payroll | 1 (`Ess`, `bm-employee`) | `Module.Ess` | — | declared (self-service) |
| file-storage | 1 (`FilesApi` health probe) | anonymous | — | declared |
| gateway | 2 (`Account`, `Home`) | anonymous / `Gateway.Dashboard` | — | declared |
| shared | 1 (`Culture`) | `Gateway.Authenticated` | — | declared |
| eight other services | 0 | no restricted role reaches the module | — | latent rows signed `IAM-H` |

**Latent rows per service (all `IAM-H: latent, no restricted role reaches it (D-IAM-3)`), 114:** payroll 24, hr-training 13, dev-analytics 12, hr-health 11, compliance 11, auth-directory 8, hr-movements 8, it-infra 8, idcard 7, records-archive 7, gateway 3, file-storage 2. HR.Core, HR.Contracts and Admin.Logistics have no list (ceiling 0).

## 5. The answers

**404 (D-52-5), each proved by the generated HTTP test (`Iam052*ScopeHttpTests`: every keyed id of every `[OrgScoped]` action, with the database read before and after) and by the scenario named:**
- HR.Core: Diwan, statistics, inspections (a foreign unit or employee on create), attendance (details, edit before and after the change, delete, create; **was 403**), rosters and assignments, key positions, succession plans and candidates, employment records, documents, qualifications, contacts, CVs, evaluations, corrections, the approvals queue, funded positions, workforce plans and lines, internal vacancies and applications, requisitions, appeals, interview slots, workflow instances' `Get`/`History` — AC-IAM-52a, 52d, 52e.
- HR.Contracts: the hire wizard's steps by `transactionId` — AC-IAM-52b; the other contracts screens (latent today) by the generated test.
- Admin.Logistics: supply orders, receipts, maintenance — AC-IAM-52c.

**403 kept (P-4), with the test that pins it:**
- `api/lookup/employees/{id}` (AC-06, `Plan034ScopedIndexTests`); `Assets/Details` and custody (AC-32); PLAN-34's twelve `Index(employeeId)` lists (`Plan034ScopedIndexTests`, AC-QA-34d: six HR.Contracts lists declared `OutOfScope = Forbid`, six Payroll lists latent with their in-action guard); `HireWizard.Index`/`Continue`/`Step1` (QA-034, AC-IAM-52b); HR.Core's employee records and documents where §2 rule 2 keeps them; transfers and loans (both units, PLAN-50); AC-EST-11's gap analysis; reports' filters return an empty report (AC-HCM-32b).

**403 by stance:** `Denied` scope (AC-IAM-52a/52d with `negative-invalid-scope`, whose only role passes `Module.HrCore`, so the 403 is the scope's); central-only screens and catalogue writes (AC-IAM-52a shifts, 52c inventory and vendors).

## 6. The records with no key

From `scripts/iam052-validate.ps1 -Counts -SshHost hrm-vps -Project hrm` (read-only, `BEGIN READ ONLY`), on the demo server before (06:5xZ) and after (07:44Z) HCM052:

| Database | Entity | No key before / total | No key after / total |
|----------|--------|----------------------:|---------------------:|
| hr_core_db | WorkflowInstances: RecruitmentRequisition | 22 / 22 | **0 / 22** |
| hr_core_db | WorkflowInstances: AssetAssignment, AssetLossReport, CardRequest, DisciplinaryAction, EstablishmentChangeRequest | 16, 8, 14, 7, 3 (all) | the same (types without a key: central by design) |
| hr_core_db | DiwanCorrespondences, FieldInspectionVisits | 0 rows (no unit column yet) | 0 rows |
| hr_core_db | HrStatisticReports, ShiftRosters, KeyPositions, InternalVacancies, Employees | 0 of 1, 1, 10, 5, 500 | the same |
| hr_contracts_db | ServiceTransactions, Contracts (through the employee snapshot) | 60 / 60, 500 / 500 | the same (0 employee snapshots: central) |
| admin_db | Assets (through the custodian's snapshot), SupplyOrders | 224 / 224, 0 / 50 | the same |
| satellites | OrgUnitSnapshots | HR.Contracts, Payroll, HR.Movements 30; Admin.Logistics, HR.Health, HR.Training **0** | the same (§11) |
From the migration test (`Iam052OrgScopeMigrationPostgreSqlTests`, a non-superuser owner under FORCE, two agencies): instances without a unit 12 → 4 (the four keyed types backfilled; the others stay central by design), Diwan 6 → 4 (rows without an employee stay central), inspections 2 → 2 (no source for a unit).

## 7. The guards and their own negative tests

| Guard | What it refuses | Its own negative test |
|-------|-----------------|-----------------------|
| `OrgScopeCoverageTests` | a controller with no stance and no list row; a row that is not latent or not signed `IAM-H`; a list that grew; an id of an `[OrgScoped]` action with no key and no declaration; a registry not built from its service's `Configure` | `GuardSelfTests` (planted: no stance, an id/list/model id without a key, a stance naming an unkeyed entity, an unsigned row, a stale row) |
| `Iam052OrgScopeKeysTranslateTests` | a key that does not translate on SQLite or PostgreSQL | every key of the four registries runs on both |
| `Iam052*ScopeHttpTests` (generated) | a foreign id that is not refused, a change in the database, a page (bare or with the user's own ids) that shows any foreign record or the foreign unit | it found two real leaks (review 2) |
| `scripts/iam052-validate.ps1` | the requirement back, a service without the filter or its `Configure`, the filter after the automatic 400, a list for the three services, an unsigned row, HCM052 with a BOM or without FORCE handling, a missing transfer consumer, AC-IAM-52 unplaced | `-SelfTest`: 10 planted defects refused |
| `scripts/iam002-validate.ps1` | the handler or requirement back; the registry, attributes and filter missing | its existing test |
| AC-QA-34d | BOLA v2 not green in the Linux run's architecture trx | Blocked when the trx is missing |

## 8. Results

- **Unit:** 6282/6282 at P3–P4; after P5–P6 the changed areas (Iam052, Iam002, Keycloak) 66/66. The full run on this report's commit is ci-local's (in the merge note: this report is committed before the local gates).
- **Architecture:** 203/203.
- **Integration (no Testcontainers):** 412 passed, 4 skipped at P4; ci-local's run in the merge note.
- **Server (P8):** `AC_IAM_52_vps_*` 4/4; the PLAN-36..51 server suites 48/49 + the WF-46 sweeps 1/1 rerun alone; smoke with mandatory wiring 50/0/1.
- **Testcontainers, full step on the host (CI's filter):** 227 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`: `HRM_MIGRATE_IMAGE` not set), 0 failed, 6 min 49 s. `Iam052*` 21/21.
- **Acceptance, local (P-14), through `scripts/ci/acceptance-job.ps1`:**
  - prod-like: journey 1/1; `main` 38/38 (AC-IAM-52 5/5, AC-IAM-50, AC-IAM-51, AC-HCM-25, AC-WF-43, AC-TEN-48, AC-POL-49); isolated AC-INT-37 8/8 (7/8 before 52b's cleanup), AC-MSG-40 8/8, AC-WF-44 5/5, AC-WF-45 8/8, AC-WF-46 9/9, AC-POL-47 1/1, AC-OPS-39 8/8; s2s42 9/9 + 1/1. One reset by the remote end in AC-IAM-50b on the second run; the class alone on a fresh stack 5/5.
  - lab: `main` 53/53 (AC-01..06, AC-32, AC-33, AC-HCM-21/22/28/29/30/31/32, AC-EST-11/18/24, AC-WF-23), journey 1/1, isolated 9/9; guard PASS.
  - leastpriv (HCM052 under the least-privileged migrator): 9/9 after AC-OPS-38f's wait fix (twice 8/9 before: the broker is healthy before its management listener is up, and the probe threw instead of waiting; PLAN-51 had 9/9 here).
  - Seeded counts: 32/32 as `acceptance-verify.ps1` expects.
- **The PR run's artifact (evidence of record):** linked in the merge note (the PR is opened after this report, as the prompt orders).

## 9. The server

**Before** (P0's picture, read-only; tag `d0e446fe3280`): one tenant, 30 units (ROOT, 9 departments, 20 sections), 500 employees; no demo user restricted; 70 workflow instances, none with a unit; satellites' employee snapshots 0; `Keycloak:DirectorySync:Enabled` false everywhere; `ITransferExecuted` bound to 7 queues, none for HR.Health.

**Changes and their undo:**

| Change | Undo |
|--------|------|
| Tag `92a69960c826` (previous `d0e446fe3280`), HCM052 in `hr_core_db` applied by `hr_core_migrator` (1 migration, 91 in history): `OrgUnitId` (nullable, index, FK SET NULL) on Diwan and inspections, requisition instances stamped (22) | `scripts/vps/deploy.ps1 -Rollback`, no SQL (the rollback note below) |
| `e2e-branch-manager` (branch-manager, hr-core, admin-logistics, offline_access; `tenant_id`; `scope_org_unit_id` = HR `11111111-1111-1111-1111-111111111102`; the demo password) by `add-demo-user.sh --scope-org-unit-id`; Keycloak's example token carries the scope | delete the user (Keycloak console, or kcadm) |
| Backup `/opt/hrm/backups/pre-plan52-20261113T064822Z` (700/600: `pg_dumpall.sql`, `env.backup`, `SHA256SUMS`; 17 `CREATE DATABASE`) | delete when no longer needed |

**Build and deploy:** upload 32.3 MB in 203 s; build 2084 s, push 38 s, 17/17 images (detached, `.done`). Deploy attempt 1 stopped on this machine before touching the server (Docker Desktop down, so the bundle's gitleaks scan could not run); attempt 2, 07:38:36Z to 07:43:17Z: pull 15 s, up 147 s, bootstrap 65 s, reconcile 17 s (0 changes), healthy 17 s; services away about 3 min at most.

**Checks** (evidence under `artifacts/plan52/vps/`): `e2e-branch-manager` opens HR's roster; his key-position list holds HR's one (= psql); Finance's key position and attendance answer 404 on GET and POST with rows unchanged; `api/workflow-instances` lists 6, all in HR, and a Finance and a central instance answer 404 to him, 200 to `hradmin`; the hire wizard lists none of the 8 appointments (no employee snapshot on the demo: central) and Step2 answers 404 to him, 200 to `hradmin`; inventory and vendor writes 403, nothing created; HR.Health's `hr-health-web-transfer-executed` has a consumer; no `DirectorySync__Enabled` override. **Admin.Logistics on the demo holds no unit snapshot** (§11), so his scope there is Denied: the order list and Finance's order answer 403 and nothing changes (fail-closed). The server suites 48/49 (AC-WF-46's sweeps read a log window before the cycle after AC-OPS-39f's recreate, as in PLAN-46..51; rerun alone after the next cycle: passed); the suites' rewritten evidence restored, copies under `artifacts/plan52/vps/acceptance-evidence-from-run/`.

**At the end** (08:43:47Z): tag `92a69960c826`, 21 containers, every one with a health check healthy, 22 GB free; smoke 50/0/1.

**Rollback note** (in `OPS_VPS_SINGLE_HOST.md`; PLAN-51's code read with `git show`): `d0e446fe3280` maps neither new column and sees no pending migration (EF applies the migrations it knows; the extra history row is harmless); what it writes during a rollback (Diwan, inspections, the instances it starts) has no unit, so it is central for a restricted user after a roll-forward, and nothing backfills it (HCM052 does not run again); its Auth.Directory transfer consumer only updates the directory; `e2e-branch-manager` stays, with the old partial scope.

## 10. Items that other plans own

- **IAM-H (wave 2):** the 114 latent rows; HR.Movements' employee snapshot (G-MOV-10), without which secondment, promotion and delegation stay central for a restricted user.
- **PLAN-53:** the payroll scope beside its frozen write list (the six PLAN-34 payroll lists keep their in-action guard).
- **PLAN-62:** the directory sync; with it on, a transfer clears the scope in Keycloak too (tested with a fake admin API).
- **PLAN-65:** AC-01..06 live (AC-02, AC-03, AC-06, AC-32 stay `required: false`; this plan treated them as red and they passed).
- **File.Storage's owner:** HR.Core does not check that a posted `fileId` is the caller's upload (review 6; pre-existing; File.Storage records the uploader).

## 11. Defects for later plans

- `DailyAttendanceRecords.Details` has no view: 500 for any in-scope record (no page links to it; found by the new page scan; on main before this plan).
- `EssTalent.Apply` visibility, `WorkforcePlans.RemoveLine` has no child–parent check (a central write), the ReferenceData versus ReferenceCatalog RBAC gap (IAM-H) — from P0's inventory.
- Messages are unordered: a late first delivery of an older transfer moves a satellite's snapshot back (HR.Contracts', Admin.Logistics', HR.Health's consumers alike); a scope cleared by Auth.Directory stays cleared (fail-closed).
- A workflow instance's unit is stamped at start, while the inbox resolves it live; they differ only if a position moves while an instance is open.
- **On the demo server, Admin.Logistics, HR.Health and HR.Training hold no org-unit snapshot** (HR.Contracts, Payroll, HR.Movements hold all 30), although their `IOrgUnitChanged` queues are bound, empty, with a consumer and no error or skipped queue, and HR.Core re-publishes every unit at each start. A restricted user is therefore Denied (403, fail-closed) in Admin.Logistics there. Pre-existing; for the plan that owns the satellites' snapshots (the study's IAM-C dependency) or wave 2.
- Admin.Logistics' start logs a `PK_Vendors` duplicate-key error from its demo seed on the server (unrelated to scope).
- AC-WF-46's sweeps check reads the last 5000 lines of a noisy log: after a recreate, a cycle can fall outside that window (PLAN-46..52).

## 12. Timing

Started 2026-11-12T20:25:49Z. To this report's commit (about 08:55Z): **wall-clock about 12 h 30 min**, of which **5 h 08 min were a stop**
and **about 7 h 20 min active**. Waiting for CI, the merge and the post-merge run are recorded in the merge note (`plan52-progress.md` on
`docs/plan-52-merge-note`), not here: this report is committed before the local gates, as the prompt requires.

**Stops:**

| From (UTC) | To (UTC) | Length | Cause, and what ended it |
|------------|----------|-------:|--------------------------|
| 01:39:37 | 06:47:xx | 5 h 08 min | outbound TCP 22 blocked from the owner's network at P8's start (`BLOCKED:`); the owner's "VPN is on" |
| 07:36:43 | 07:38:02 | 1 min | Docker Desktop down on this machine (its disk image compacted 245 → 82 GB at 07:32Z); it came back on its own |

**Build, acceptance and deploy:**
- Local images and the first prod-like stack 23.5 min (build and start); a prod-like stack from empty volumes 408 s; the image rebuild after the review 15 min; leastpriv with the migrator rebuilt 16 min.
- Local acceptance (P6): prod-like run 1 23 min, lab 26 min, prod-like run 2 19 min, AC-IAM-50 alone 9 min, leastpriv 16 + 6 + 6 min.
- Testcontainers full step on the host: 6 min 49 s (tests); `Iam052*` alone about 5 min.
- Server: upload 203 s, build 2084 s, push 38 s; deploy 4 min 41 s (services away about 3 min); the PLAN-52 server checks about 4 min (first run); the server suites 43 min 31 s; the WF-46 rerun 18 s.
- Actions minutes: in the merge note (the PR run).

**`scripts/plan-timing.ps1 -From 25 -To 52`** (Riyadh time; PLAN-52's line is before its merge):

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15   6:15
PLAN-46      14 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 15:06  False 15:31  15:32
PLAN-47      15 2026-11-08 15:06 2026-11-08 15:06 2026-11-09 15:30  False 24:23  24:24
PLAN-48      12 2026-11-09 18:27 2026-11-09 18:28 2026-11-10 02:18   True 7:50   7:51
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58  17:00
PLAN-50      14 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08  10:09
PLAN-51      15 2026-11-12 02:26 2026-11-12 02:30 2026-11-12 20:04   True 17:34  17:38
PLAN-52       9 2026-11-12 23:25 2026-11-12 23:26 2026-11-13 11:45  False 12:18  12:19
```

(PLAN-25..44 as printed by the script are unchanged from PLAN-50's report.)

## 13. Readiness

- **PLAN-53:** the registry and the filter are ready for Payroll: declare `[OrgScoped]` on its controllers (their rows leave the list), register `AddOrgScopeFilter<PayrollDbContext>` with keys through `EmployeeOrgUnitSnapshot`; the frozen write list is unaffected.
- **PLAN-62:** turn `Keycloak:DirectorySync:Enabled` on; a transfer then removes `scope_org_unit_id` in Keycloak (whatever the local value), and a failure retries the message.
- **PLAN-65:** AC-01..06 and AC-32 pass live locally; their `required` flags are yours.
- **Wave 2 (IAM-H):** each latent service declares its stances and leaves its list (the coverage test only lets a list shrink); HR.Movements needs an employee snapshot before its three employee-only types can be scoped.
