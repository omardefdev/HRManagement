# PLAN-51 final report — an explicit policy for every action, and no wide paths or wide policies left (IAM-E)

Plan: [PLAN-51-iam-explicit-policy-every-action.md](../docs/acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md).
Prompt: [PLAN-51-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-51-AGENT-PROMPT.md). Progress and every stamp:
[plan51-progress.md](plan51-progress.md). Baseline: [plan51-baseline.md](plan51-baseline.md).

## 1. Summary per phase

| Phase | What it did | Finished (UTC) |
|-------|-------------|----------------|
| P0 | Keep-awake, preconditions (SSH failed: decision 1), baseline build 0/0 and listed filter 486/486; inventories 0.4 (688 writes, 323 without a policy, +9 roles only, +1 shared), 0.5 (service calls into HR.Core), 0.6 (employee and marker pages), 0.7 (the filter's name list) | 2026-11-11T23:45:16Z |
| P1 | `HRManagement.ArchitectureTests`: one project over the fifteen web assemblies plus the shared controllers; lists seeded with their owners and pinned ceilings; planted-defect self tests | 2026-11-11T23:55:59Z |
| P2 | Self-service: the gateway decides `/ui/ess` and `/api/ess` by exact segment (named controllers, static files, otherwise 404 + `Denied:EssNotExposed`); `HrCore.Lookup` for staff and services; HR.Core static files on `Gateway.Authenticated`; PATCH counts as a write at the gateway | 2026-11-12T00:06:52Z |
| P3 | `Gateway.Audit` (configured roles) on the audit, operations and metrics pages; the agency filter inside `GatewayAuditQueryService`; one configured-roles helper for D-51-2, D-IAM-4 and D-IAM-7 | 2026-11-12T00:18:21Z |
| P4 | The module filter without a name list (override or module policy, then the module's write policy on an unnamed write); `{Module}.Write`/`{Module}.Approve` in every module; 307 actions named; sensitive actions on approval policies; HR.Movements binds view models; D-IAM-4 in Production/Staging/prod-like, off in demo mode; `HrCore.BranchOperation` deleted | 2026-11-12T00:46:32Z |
| P5 | `hr-core.workflow` accepted on three endpoints only; onboarding on `Platform.AgencyOnboarding`, `apply-file` deleted; evidence ingest on `Evidence:IngestClients` (empty) | 2026-11-12T01:07:30Z |
| P6 | AC-IAM-51a..f and two personas; the P-11 class lists on prod-like, least privilege and lab; one independent review (no high, 2 medium, 7 low, all handled or recorded) | 2026-11-12T03:10:24Z |
| P7 | `origin/main` had not moved (`0db7292e`): no merge | 2026-11-12T03:10:48Z |
| P8 | SSH blocked from P0; resumed when port 22 passed (through ProtonVPN). Verified backup, read-only before-picture, one detached build of `d0e446fe3280`, deploy on the first attempt, the four PLAN-51 server checks 4/4, the server suites 44/45 + the WF-46 sweeps rerun alone, final self-test 50/0/1 | 2026-11-12T08:18:30Z |
| P9 | Docs, the report, local gates, the release branch, the PR and its merge | in the merge note |

## 2. Commits

- `06d127dd` docs(iam050): merge note for PR 48
- `c1f60b3e` docs(iam051): PLAN-51 agent prompt
- `89565db2` chore(iam051): baseline, the writes without a policy per service, service calls, the pages of employees and markers, the name list, the server's audit rows and configuration
- `f5b181a8` test(iam051): one architecture project over the fifteen web assemblies; writes, sensitive actions, internal routes, overrides and entity bindings checked against ceiling lists that only shrink
- `530d139c` feat(iam051): self-service reaches its own controllers only; the employee directory is for staff and services; internal routes stay service-only
- `7b9589a1` feat(iam051): the gateway's audit log is for its owners, and each agency sees its own rows
- `4529780a` feat(iam051): no module name list; every write in every service names its policy (Payroll waits for PLAN-53), sensitive actions their approval policy; HR.Movements binds view models, not entities
- `fe12fb78` feat(iam051): hr-core.workflow reaches three endpoints only; agency onboarding is the platform administrator's and reads no file; evidence ingest takes a client list
- `9d130248` test(iam051): AC-IAM-51a..f pass on the prod-like stack; review fixes; CI scope and ownership updated
- `5fcc6c6b` docs(iam051): gateway self-service and audit, where hr-core.workflow is accepted, the architecture project and its lists, the study's status lines; checkpoint before the server phase
- `66badede` docs(iam051): progress, stopped at P8 waiting for SSH to the demo server
- `d7a51db1` docs(iam051): progress, P8 still waiting for port 22 from this machine
- `d0e446fe` docs(iam051): progress, SSH retry at 05:41Z still times out
- `8136e74a` test(iam051): demo server on explicit policies (self-service closed, the audit for its owners, onboarding for the platform), PLAN-36..50 server checks still green

## 3. Decisions applied and deviations

Decisions D-51-1..5, D-IAM-4 and D-IAM-7 are applied as the plan states them. Decisions taken in the session (numbers as in the progress
file):

1. **SSH to the demo server** was blocked from P0 (outbound TCP 22 timed out from this machine's network, to github.com too). The local
   phases went on; P8 stopped twice with `BLOCKED:` and ran when the owner's network let port 22 through (through ProtonVPN).
2. **The shared `CultureController`** (ServiceDefaults) is scanned as a sixteenth entry, `shared`, so no write in a shared controller
   escapes the lists; `Culture.SetCulture` names `Gateway.Authenticated`.
3. **HR.Core's five sensitive writes that carried `HrCore.Write`** (`Competitions.PublishResults`, `OfferLetters.Revoke`,
   `RecruitmentRequisitions.Publish/PublishInternal`, `WorkforcePlans.Approve`) take a configurable `HrCore.Decision` with today's roles
   (hr-admin, hr-core): `HrCore.Approve` would have widened them.
4. **`Authorize(Roles=…)` alone is not a named policy**: the nine roles-only writes take `Compliance.Dsar` and `Platform.Operator`, with
   the same roles.
5. **`/ui/ess` with no controller** answers 404 too; the user guide's `/ui/ess/EssPortal` passes.
6. **Deviation from P-9 ("adapt, never weaken"):** `Iam050MovementApprovalPolicyTests.The_override_never_sits_on_a_write_action` forbade a
   class-level override in five assemblies, while P-6 requires class-level overrides on the name list's controllers. The movement
   controllers still may not carry one; elsewhere the test now requires every write under a class override to name its own policy, which
   is stricter for those writes.
7. **`hcm025-validate` failed on `origin/main`** (PLAN-50 renamed `GetBlockReasonAsync`): the check accepts both names.
8. **File.Storage** never had the module filter (its writes all name policies); it gets no write default and its list is pinned at 0.
9. **Writes that named only a read policy** (found by a catalog pass and by review M1) now name their write policy, or are declared POST
   reads with a reason (22, pinned).

No deviation defers part of the plan.

## 4. The writes, before and after

AC-IAM-51d proves it end to end (a reader and the approver write nothing; the module's writer does). "No policy" = a POST/PUT/PATCH/DELETE action with no `[Authorize(Policy=…)]` on the action or its class and no `[AllowAnonymous]`
(baseline 0.4). "After" counts the same way, from `HRManagement.ArchitectureTests` on HEAD.

| Service | Writes | No policy before | No policy after | Write list rows (ceiling) | Unnamed writes meet (default roles) |
|---------|-------:|-----------------:|----------------:|--------------------------:|-------------------------------------|
| HR.Core | 275 | 42 | 0 | 0 (0) | `HrCore.Write`: hr-admin, hr-core (no service branch) |
| HR.Contracts | 64 | 41 | 0 | 0 (0) | `HrContracts.Write`: hr-admin, hr-contracts |
| Payroll | 61 | 16 | 16 | 16 (16, PLAN-53) | `Payroll.Write`: hr-admin, payroll (the filter applies it to the 16 at run time) |
| HR.Movements | 36 | 8 | 0 | 0 (0) | `HrMovements.Write`: hr-admin, hr-movements |
| HR.Training | 32 | 30 | 0 | 0 (0) | `HrTraining.Write`: hr-admin, hr-training |
| HR.Health | 30 | 24 | 0 | 0 (0) | `HrHealth.Write`: hr-admin, hr-health, hr-health-clinical |
| Compliance | 36 | 32 (+4 roles only) | 0 | 0 (0) | `Compliance.Write`: hr-admin, compliance; DSAR on `Compliance.Dsar` |
| IDCard | 27 | 27 | 0 | 0 (0) | `IdCard.Write`: hr-admin, idcard |
| Admin.Logistics | 30 | 30 | 0 | 0 (0) | `AdminLogistics.Write`: hr-admin, admin-logistics |
| IT.Infra | 25 | 25 | 0 | 0 (0) | `ItInfra.Write`: hr-admin, it-infra |
| Records.Archive | 24 | 24 | 0 | 0 (0) | `RecordsArchive.Write`: hr-admin, records-archive |
| Dev.Analytics | 19 | 17 | 0 | 0 (0) | `DevAnalytics.Write`: hr-admin, dev-analytics, platform-admin |
| Auth.Directory | 8 | 7 | 0 | 0 (0) | `AuthDirectory.Write`: hr-admin, auth-directory |
| File.Storage | 14 | 0 | 0 | 0 (0) | no filter; upload and delete on `FileStorage.Write` (the module's assertion) |
| Gateway | 7 | 0 (+5 roles only) | 0 | 0 (0) | `Platform.Operator` (platform-admin) |
| shared (`Culture`) | 1 | 1 | 0 | 0 (0) | `Gateway.Authenticated` |
| **Total** | **689** | **324 (+9 roles only)** | **16 (Payroll)** | **16** | |

Every policy above reads `Authorization:Policies:{name}:Roles` first (D-51-2), so an agency can separate makers from checkers by
configuration; none widens today's roles. Sensitive actions: 0 rows outside an approval policy (36 on `{Module}.Approve`, five on
`HrCore.Decision`, `Payroll.Post`/`Payroll.Reverse` as before); the sensitive list is gone. Entity bindings: HR.Movements 12 -> 0 (view
models in `Models/MovementForms.cs`); 113 rows remain in nine services, each signed IAM-F (G-IAM-10).

## 5. The narrowed paths and policies

| Path or policy | Before | After | Proved by |
|----------------|--------|-------|-----------|
| `/ui/ess`, `/api/ess` | any HR.Core controller under `Module.Ess` (the gateway matched `Contains("/Ess")`) | the named controllers (`EssPortal`, `EssTalent`; Payroll `Ess`), GET static files, otherwise 404 + `Denied:EssNotExposed` | `GatewayEssRoutingTests` (62), AC-IAM-51a, `AC_IAM_51_vps_self_service_…` |
| `/Ess` elsewhere | partial match gave `Module.Ess` | exact segment only (`/EssXyz`, `/ProcessEss` are not self-service) | `GatewayEssRoutingTests` |
| The directory (`HrCore.Lookup`) | any signed-in user | staff (PLAN-50's definition) or a service with `hr-core.lookup` | `ServiceToServiceNegativeMatrixTests`, AC-IAM-51b |
| `api/internal/*` | service-only by convention | no route accepts an interactive user | `InternalRoutesAreServiceOnlyTests`, `Iam051DirectServiceWriteTests`, AC-IAM-51b |
| The gateway's audit | `Gateway.Dashboard` (any signed-in user), every agency | `Gateway.Audit` (platform-admin all, auditor and compliance their own agency) | `Iam051GatewayAuditScopeTests`, `Iam051GatewayAuditPostgreSqlTests`, AC-IAM-51c, `AC_IAM_51_vps_the_audit_…` |
| `hr-core.workflow` | met `HrCore.Write` (every HR.Core write) | `Workflow.ExternalStart`, `Service.CodeGenerate`, `Service.PolicyEnforce` (+ the reads) | `Iam051DirectServiceWriteTests`, `FineGrainedPolicyTests`, AC-IAM-51e |
| Onboarding | `HrCore.Write` (hr-admin) and `apply-file` read a server path | `Platform.AgencyOnboarding` (platform-admin); `apply-file` deleted | `Iam051OnboardingEvidenceTests`, AC-IAM-51f, `AC_IAM_51_vps_onboarding_…` |
| Evidence ingest | any client with `evidence.ingest` | the clients in `Evidence:IngestClients` (empty) | `Iam051OnboardingEvidenceTests` |
| D-IAM-4 | hr-admin met `HrHealth.Clinical`, `Payroll.Post`, `Payroll.Reverse` | excluded in Production/Staging/prod-like, kept in demo mode | `Iam051ModuleFilterAndPoliciesTests` (the real Production, Staging and Development files and the prod-like compose), `AC_IAM_51_vps_demo_mode_…` |

## 6. The guards and their own negative tests

`HRManagement.ArchitectureTests` (131 tests): `WriteActionsHaveExplicitPolicyTests` (every write names a policy that is not only a read
policy, or is a declared POST read), `SensitiveActionsHaveApprovalPolicyTests`, `InternalRoutesAreServiceOnlyTests`,
`ModuleOverridesAreDeclaredTests`, `MassAssignmentArchitectureTests` (entities, arrays, collections and a bound model's entity properties;
`[BindNever]` honoured), `AllWebAssembliesLoadTests`. `GuardSelfTests` plants each defect (an unnamed write, a write under a read policy,
a sensitive action on a write policy, an internal route for users, an undeclared override, an entity array and a model with an entity
property) and proves each guard fails on it. A list that loses a row fails until the row is removed (`The_list_only_shrinks`); a ceiling
can only go down. `scripts/iam051-validate.ps1 -SelfTest`: 9 planted defects, all caught. `hcm025-validate` refuses the old HR.Core list,
`wf043-validate` refuses `HrCore.BranchOperation`.

## 7. Results

| Suite | Result |
|-------|--------|
| Unit (`HRManagement.UnitTests`) | 6270/6270 |
| Architecture | 131/131 |
| Integration without Testcontainers | 402 passed, 4 skipped |
| Testcontainers (full CI filter, host) | 216 passed, 1 skipped, 0 failed |
| Acceptance prod-like (P-11 list) | runs 1..4: AC-IAM-51a..f + catalog fact 7/7; AC-IAM-50 5/5; AC-HCM-25, AC-WF-43, AC-TEN-48, AC-POL-49, AC-INT-37 8/8, AC-MSG-40, AC-WF-45, AC-WF-46, AC-POL-47 green |
| Acceptance least privilege | 9/9 |
| Acceptance lab | main 27/27 (AC-07, AC-HCM-28, AC-HCM-31 among them), journey 1/1, AC-HCM-26 5/5, AC-HCM-27 4/4 |
| Server (P8) | `AC_IAM_51_vps_*` 4/4; the PLAN-36..50 server suites 44/45 + the WF-46 sweeps 1/1 rerun alone; smoke with mandatory wiring 50/0/1 |
| ci-local and pre-push on the final HEAD, and the PR run | [plan51-progress.md](plan51-progress.md) (P9, and the merge note); the PR run's Acceptance artifact is the evidence of record |

The Testcontainers step ran at 03:10Z..03:19Z on the code of 9d130248; nothing it builds or tests changed after it (the later commits are
docs, progress and the server check class of the acceptance project).

## 8. The server

**Before (read-only, 06:53Z..06:55Z, `artifacts/plan51/vps/before.txt`):** tag `a504f6e54921` (on `origin/main`), previous
`046316bde9e7`; 22 containers, 20 healthy (two without a health check); 38 GB free. 10095 gateway audit rows, **all without an agency**;
last 7 days `allowed` 7146, `forbidden` 40, `client_error` 39, `server_error` 4. Every web container runs Production with
`Deployment__Mode=demo`; no container sets an `Authorization__*`, `Gateway__Ess` or `Gateway__Audit` key, so the images' settings decide.

**Changes, in order, with their undo:**

| # | Change (UTC) | Undo |
|---|--------------|------|
| 1 | Backup `/opt/hrm/backups/pre-plan51-20261112T065411Z` (700; `pg_dumpall.sql` 29.8 MB, `env.backup`, `SHA256SUMS`: 600); 17 `CREATE DATABASE`, the completion line, `sha256sum -c` OK (06:54:11..06:54:17) | none; delete the folder when unwanted |
| 2 | One build of `d0e446fe3280` on the server, detached (`setsid nohup`, `.done` file): upload 32.2 MB in 50 s, build 1817 s, push 27 s, 17/17 images (06:56:12..07:27:56); 31 GB free after | none (the previous tag's images stay) |
| 3 | Deploy `scripts/vps/deploy.ps1 -Tag d0e446fe3280`, first attempt (07:28:21..07:31:43 with the self-test): pull 3, up 117, bootstrap 51, Keycloak reconcile 14 (0 changes), healthy 14; healthy at 07:31:05; services away at most about 3 min 16 s; self-test 50 pass, 0 fail, 1 warning (the client-side port probe) | `scripts/vps/deploy.ps1 -Rollback` |

No migration, no `.env` or compose key, no realm change (D-51-4). The deployed tag is the code of the final HEAD: the commits after
`d0e446fe` change only tests and documents, none of which enters an image.

**After (checks, `artifacts/plan51/vps/`):**
- `AC_IAM_51_vps_*` 4/4: self-service `POST /ui/ess/Shifts/Create` and `GET /ui/ess/api/internal/employees/{id}/lifecycle-flags` 404,
  both audited `Denied:EssNotExposed`, no shift created; `/ui/ess/EssPortal` 403 for `e2e-compliance`; `e2e-hr-admin` refused on
  `/Home/Audit` and `/ops/audit-search` (302 to the access-denied page); `e2e-compliance` 200 on both, 48 rows seen, 0 of another agency or
  none, while 826 rows of the last 24 h lie outside its agency; `hradmin` 403 on onboarding apply (dry run), `apply-file` 404; Payroll and
  HR.Health run `Deployment__Mode=demo` without an exclusion in the environment (the Production file lists it; off in demo mode). The first
  three runs of these checks failed in the checks, not the server (sign-in path for a user without HR.Core, the refusal's redirect, a row
  from a page the gateway does not audit); each fix kept the check's meaning (progress file, P8 item 4).
- The server suites (07:41:09..08:16:25, 35 min 16 s): 44/45; by check OPS-36 10/10, IAM-50 4/4, POL-49 3/3, OPS-39 2/2, WF-46 3/4,
  POL-47 2/2, WF-45 3/3, MSG-40 3/3, IAM-51 4/4, WF-44 3/3, WF-43 2/2, OPS-38 1/1, TEN-48 1/1, INT-37 3/3. `e2e-checker-mgmt` still writes in
  HR.Core and `e2e-maker-contracts` in HR.Contracts (PLAN-50's and PLAN-38/49's checks), and the service calls (custody starts, code
  generation, policy queries, lookups, delegations) pass. The WF-46 sweeps check read the card service's log before its first cycle after
  AC-OPS-39f recreated the services (as in PLAN-46..50) and **passed rerun alone** (08:16:38..08:16:52).
- At the end (08:17:45): tag `d0e446fe3280`, 20 containers healthy, 31 GB free, smoke with mandatory wiring 50/0/1. The committed
  `acceptance-evidence/` the suites rewrote (41 entries) is restored; a copy is under `artifacts/plan51/vps/suites/`.

**What the demo users see differently:** `hradmin` and `e2e-hr-admin` lose the gateway's audit and operations pages (the menu link is
gone; opening one directly ends on the access-denied redirect, a 404 page); `e2e-compliance` gains them, with its own agency's rows only,
and only rows written from this deploy on carry an agency. Nobody on the demo calls onboarding any more (no demo user holds
`platform-admin`), and `apply-file` is gone. Self-service answers 404 for anything but its own pages, for every user. Writes are unchanged
for every demo user (`hradmin` keeps the clinical and payroll post/reverse policies in demo mode).

**Rollback note** (`docs/OPS_VPS_SINGLE_HOST.md`, PLAN-51): `scripts/vps/deploy.ps1 -Rollback` to `a504f6e54921`, no SQL, no restore.
The previous tag has no migration to skip; it ignores the new rows' `TenantId` (no agency filter, its audit page open to any signed-in user
under `Gateway.Dashboard`), shows `Denied:EssNotExposed` raw, and its images carry their own settings, so the new keys leave with the new
images. Backup restore only if data were damaged.

## 9. Items that other plans own

- **PLAN-53:** Payroll's 16 writes without a named policy (`tests/architecture/write-policy-allowlist/payroll.txt`, ceiling 16), and
  `PayrollReports.Payslip`, a GET that marks a payslip sent (review L2).
- **IAM-F (G-IAM-10):** 113 entity bindings in nine services, each pinned.
- **IAM-G:** `hr-admin`'s breadth (it still meets every module's write and approve policy, except D-IAM-4's three in production).
- **IAM-H:** learning self-service (`EssLearning` stays on `Module.Ess` in HR.Training; the gateway's ess segment is HR.Core's and Payroll's).
- **PLAN-65:** `SelectionCommittees.UpsertDecision` and `*.RestartApproval` are HR workflow steps on the write policy (review L5).

## 10. Defects for later plans

- The gateway's audit rows written before this deploy carry no agency (the queue's copy dropped it since the first commit); only
  platform-admin sees them. No backfill: the agency of an old row cannot be read back reliably from its user name.
- A refusal of the gateway's own pages (`/Home/Audit`, `/ops/audit-search`, the platform pages) answers 302 to
  `/Account/AccessDenied`, not 403, and the gateway has no such page: a refused user lands on a 404 (seen on the demo server for
  `e2e-hr-admin`). The refusal holds (AC-IAM-51c and the server check accept 403 or that 302, never the page); the page, or a 403 from the
  cookie scheme's access-denied event, is for a later plan. It existed before PLAN-51 for the platform pages; PLAN-51 makes `hr-admin`
  meet it on the audit, whose menu link it no longer shows.
- The gateway audits what it forwards, not its own pages: who opened the audit page itself is not recorded.

## 11. Timing

Started 2026-11-11T23:26:13Z. To this report's commit (about 08:30Z): **wall-clock about 9 h 04 min**, of which **3 h 20 min were stops**
and **about 5 h 44 min active**. Waiting for CI, the merge and the post-merge run are recorded in the merge note (`plan51-progress.md` on
`docs/plan-51-merge-note`), not here: this report is committed before the local gates, as the prompt requires.

**Stops (all one cause: outbound TCP 22 blocked from the owner's network, so no SSH to the demo server):**

| From (UTC) | To (UTC) | Length | What ended it |
|------------|----------|-------:|---------------|
| 03:23:13 | 05:17:18 | 1 h 54 min | the owner's "SSH fixed, resume" (still blocked: retried 9 times to 05:25:57) |
| 05:25:57 | 05:41:53 | 16 min | the owner's "Retry SSH now" (still blocked) |
| 05:42:53 | 06:53:05 | 1 h 10 min | the owner's "Retry SSH now": port 22 passed through ProtonVPN |

**Build, acceptance and deploy:**
- Local acceptance (P6, six runs): run 1 with image builds 38 min (stack 261 s on its restart), run 2 23 min, runs 3..6 with `-NoBuild`
  9, 5, 3 and 17 min.
- Testcontainers full step on the host: 9 min with the Release build (tests 4 min 22 s).
- Server: upload 50 s, build 1817 s, push 27 s; deploy 3 min 22 s with the self-test (services away at most about 3 min 16 s); the
  PLAN-51 server checks 1 min 15 s (fourth run); the server suites 35 min 16 s (PLAN-50: 1 h 22 min); the WF-46 rerun 14 s.
- **What the architecture project adds:** it compiles in 5 to 24 s and a warm run takes 1.4 s (tests 0.4 s); the first run after a build
  takes about 2 min 25 s on this machine, outside the tests (host start-up over the fresh assemblies). In CI it is one step after the unit
  tests.
- Actions minutes: in the merge note (the PR run).

**`scripts/plan-timing.ps1 -From 25 -To 51`** (Riyadh time; PLAN-51's line is before its merge):

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15   6:15
PLAN-46      14 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 15:06  False 15:31  15:32
PLAN-47      15 2026-11-08 15:06 2026-11-08 15:06 2026-11-09 15:30  False 24:23  24:24
PLAN-48      12 2026-11-09 18:27 2026-11-09 18:28 2026-11-10 02:18   True 7:50   7:51
PLAN-49      11 2026-11-10 11:35 2026-11-10 11:37 2026-11-11 04:35   True 16:58  17:00
PLAN-50      14 2026-11-11 13:00 2026-11-11 13:02 2026-11-11 23:10   True 10:08  10:09
PLAN-51      13 2026-11-12 02:26 2026-11-12 02:30 2026-11-12 11:18  False 8:47   8:51
```

(PLAN-25..44 as printed by the script are unchanged from PLAN-50's report.)

## 12. Readiness

- **PLAN-52 (branch scope):** every write now names one policy, and the configured-roles helper reads `Authorization:Policies:{name}:Roles`;
  branch scope can be added as a second requirement on those named policies, without the name list the filter used to keep.
- **PLAN-53 (Payroll):** the frozen list `tests/architecture/write-policy-allowlist/payroll.txt` (16 rows, ceiling 16) must reach 0;
  `Payroll.Write` exists and the filter already applies it to an unnamed write at run time.
- **Wave 2:** the 113 pinned entity bindings (IAM-F), `hr-admin` (IAM-G), learning self-service (IAM-H).
