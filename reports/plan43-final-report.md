# PLAN-43 final report: workflow definition governance, a guarded engine API, and separation of duties

Branch `feature/plan-43-wf-definition-governance-engine-api`, cut from `origin/main` at 7480843e (PLAN-42 merged), normal mode, in the primary clone. Plan: [PLAN-43](../docs/acceptance-plans/PLAN-43-wf-definition-governance-engine-api.md). Progress with every stamp and undo: [plan43-progress.md](plan43-progress.md). Review: `artifacts/plan43/review.md` (not committed).

**Outcome:** workflow definitions are written and published only by `workflow-admin` or `hr-admin`, from the national tenant, with a second person and a validator. A published version never changes, and every start honours the validity window. Only the owning service account starts a registered type, with the real human requester recorded; no service account acts on a step. Custody events carry the instance id and their consumers refuse an unlinked instance. Step roles are enforced for every type except five recorded exemptions, and the requester, excluded users and the subject employee cannot act. The demo server runs this code.

Three things the owner should read:

- **The first server deploy showed a defect no test had caught.** The migration's backfill changed nothing on an existing database, because the migrator role sees no rows under forced row security. It is fixed by a start-up patch, at the cost of a second server build (section 3).
- **About 4 minutes of demo downtime** on 2026-11-06, from 14:56Z to 15:00Z, when `keycloak-reconcile` failed right after PostgreSQL restarted. Running the same command again worked (section 7).
- **The early start did not happen.** The session opened in the primary clone with no second clone, so it stopped at its first precondition, and the owner chose to wait for PR 37 (section 8).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | Baseline, definitions, callers, entity types and who can reach them, server before-picture; no Stop |
| P1 | Definition lifecycle, validator, guardrails, policies, gateway rules, validity window, `EnsureVersionAsync`, health report, personas and role |
| P2 | `ExternalApprovalService`, start and cancel, requester from the header, empty allow-list, custody events v2 and the four consumers |
| P3 | Exemption semantics, `WorkflowSodRules`, evaluator and panel on the same rules |
| P4 | 18 integration tests on PostgreSQL, RabbitMQ and the real HR.Core pipeline |
| P5 | AC-WF-43a..f 6 of 6; the whole job for both profiles green |
| P6 | `origin/main` did not move: nothing to do |
| P7 | Demo server on `3bd9ee92bcf5`; every server check green |
| P8 | Docs, one independent review (no high finding), this report, the PR |

## 2. Commits

| Commit | Subject |
|---|---|
| 73a46d1a | PLAN-42's merge note, carried here as its rule says |
| 83a82eef | chore: baseline and inventories |
| 7611117a | feat: definitions governed, published by a second person, validity window |
| d8a4ac17 | feat: owner-only start, real requester, no service account on a step, custody outcomes linked |
| 9f2a030b | feat: step roles by default for every type, the three rules |
| 88bb8714 | test: proved on PostgreSQL |
| a64f7a67 | test: AC-WF-43a..f, scope and ownership, validate script, documents |
| 9b8f71ce | fix: review findings |
| 471c5b14 | test: AC-WF-43a..f and the suites green |
| c6faf0a9 | fix: one HR.Core host for the API boundary tests |
| 9c92b57a | fix: the status backfill runs at start-up, inside the tenant |
| 3bd9ee92 | test: the server check retries past the seeded requisition numbers (the tag on the server) |
| 1ad6536d | test: demo server |

## 3. Decisions applied and deviations

D-WF-4, D-WF-8 and P-1 to P-13 applied. Deviations, each with its reason:

- **Normal mode, not early start.** See section 8.
- **Two migrations**, `WF043_DefinitionGovernance` and `WF043_ExternalStart` (P-3).
- **No data backfill in the migration.** The plan asks for one. `WorkflowDefinitions` has forced row security and `hr_core_migrator` carries no tenant, so the UPDATE matched no row on the server. Testcontainers connects as a superuser and the acceptance stack seeds after migrating, so neither could show it. `WorkflowPackSeeder.BackfillGovernanceAsync` does it at every start, inside the tenant, and also repairs a version the previous tag publishes after a rollback.
- **Two server builds**, against "build once": the backfill defect above.
- **`WorkflowDefinitionRead`** instead of `HrCoreReadOnly` for reads. A persona holding only `workflow-admin` does not pass that policy, and adding the role to it would open all HR.Core reads. `WorkflowDefinitions` also joined the list of `HrModuleAuthorizationFilter`, which refused the role before the controller's policy; only the real pipeline showed that.
- **`WorkflowExternalInstancesController`.** Start and cancel moved to their own controller, on the same routes: the class demands antiforgery and the two bearer-only actions opt out, as PLAN-42 did, so the CodeQL gate holds without growing the baseline. Entries 061, 062 and 063 are marked Fixed.
- **Antiforgery on definition writes**, with `GET api/workflow-definitions/antiforgery`, the PLAN-40 pattern.
- **`HrCoreBranchOperation` lost its service clause**, since every user of it approves or rejects a step, and the evaluator refuses a service account as well.
- **Four more exemptions under P-4:** `SeparationRecord`, `DisciplinaryAction`, `CardRequest` and `Ticket`, until PLAN-46. Their step roles cannot reach approval. Nothing starts these types today, so the exemptions change nothing now; if the owner prefers, they can be dropped with no effect on running work.
- **`keycloak-reconcile` did not create realm roles**, against P-8's assumption. It now creates the manifest's missing roles, deletes none and leaves role mappings alone. On the server it also created six older roles the realm lacked.
- **Additions from design and review:** every editor counts as an author; a new version keeps the entity type of its code; `retire` needs a newer valid version; a conditional step does not count towards the limits and the last step has none; custody keeps distinct approvers by its guardrail; an external start never completes with no approver; a key reused for another entity is a conflict; a loss report names the holder as the subject.
- **AC-11:** the requester is `b-logistics`, because D-WF-8 bars a requester from every step. The test is renamed.
- **AC-WF-43e** starts on the service identity with `X-Acting-User` for `br1-mgr`, on a real pending assignment. A branch manager's scope guard needs an open holder, which an asset in storage does not have, so the UI cannot reach it.
- **`GATEWAY_CALLER_PROOF_SECRET`** removed from the server's `.env`, as PLAN-42 deferred to the next deploy.

## 4. The inventories

Definitions, callers and entity types are in [plan43-baseline.md](plan43-baseline.md). In short:

- **17 definitions**, 16 published. `security` and `it-support` are outside the catalog and the realm.
- **One production caller** of the start: `WorkflowTriggerService`. No user-facing caller, so no Stop under P-5. No test started through HTTP.
- **Exemptions:** `AssetLossReport` (PLAN-45); `SeparationRecord`, `DisciplinaryAction`, `CardRequest`, `Ticket` (PLAN-46). Each is in `docs/qa/workflow-step-role-exemptions.json`, with a review date, under the obligation `WF043-STEP-ROLE-EXEMPTIONS`.
- **`AC-S2S` M3 is closed for the registered types.** What stays for PLAN-51: this holds for this API only. `HrCoreWrite` keeps its service clause, so an `hr-core.workflow` token can still submit records that start through the coordinator.

## 5. The guards and their negative tests

| Guard | Proves | Its negative |
|---|---|---|
| `scripts/wf043-validate.ps1` | policies refuse services, no old key, no custody start elsewhere, empty allow-list | a text inventory; each rule below has a test that fails on the real behaviour |
| `Every_definition_action_that_is_not_a_read_needs_the_admin_policy_and_antiforgery` | no definition write without the admin policy and a token | reads every action by reflection |
| `Each_settings_file_holds_exactly_the_recorded_exemptions`, `Every_exemption_is_registered_...` | the settings and the registry agree | a type added in one place fails |
| `The_role_list_holds_every_role_constant` | `HrManagementRoles.All` is complete | a new constant fails |
| `The_panel_and_the_server_judge_alike` | one verdict, for requester, excluded, subject, free and distinct | each is a case |
| `Wf043ApiBoundaryIntegrationTests` | 403 for a service on a step, a user on the start, hr-core on a definition | each is a negative through the real pipeline |
| `AC_WF_43_vps_definitions_...` | the data on a real server, not only the schema | it is what caught the backfill |

`wf043-validate.ps1` has no test of its own that feeds it a bad tree: PLAN-42's guard does the same by text, and the behaviour behind each line is tested above.

## 6. Results

| Run | Result |
|---|---|
| Build `-warnaserror` | 0 warnings, 0 errors |
| Local CI on c6faf0a9 | 91 steps passed, 0 failed, 1030 s: unit 4994, integration 366, Testcontainers 148, all 0 failed. A first run failed 171 integration tests: my API boundary class built a host per test and passed EF's limit of 20 service providers |
| `Wf043` integration | 18 of 18 |
| AC-WF-43a..f alone | 6 of 6, 16 s |
| Whole job on a64f7a67, `-Profile Both` | PASS, guard 69 classes in 12 trx files, 3473 s |
| After the review fixes, images rebuilt: AcWf43, AcHcm25, AcOps39, AcMsg40, AcS2s42 | PASS, 1206 s |
| `Hcm025PanelServerParityTests`, AC-HCM-25a..h | green, unmodified |
| `wf043-validate`, `hcm025-validate`, dated obligations, EF pending-model gate (15 contexts) | PASS |

**Lab-main in the local job: 7 failures, none required.** Ac21, Ac23, Ac24 and Ac25 failed the same way in the PLAN-41 baseline. Ac08, Ac19 and Ac30 failed only here: Ac08's script uses PowerShell 7 syntax and Ac30 lost its SQL quoting under 5.1, on a machine with no `pwsh`; Ac19's cause was not read, but all three passed in PR 37's CI.

`Hcm025ApprovalGatesTests.Non_enforced_entity_types_keep_their_previous_behaviour` was rewritten: it asserted the behaviour section 3 changes on purpose. It is not an AC-HCM-25 scenario or the parity test.

**Evidence of record:** the PR run's artifact, linked in the merge note in `reports/plan43-progress.md`. No evidence is committed.

## 7. The server

| | Before | After |
|---|---|---|
| Tag | `a7ad95e0a4fa` | `3bd9ee92bcf5` (previous `c6faf0a92e6e`) |
| `WorkflowDefinitions` | 17 rows, no status | 16 Published, 1 Retired; distinct approvers on ASSET-ASSIGN-APPROVAL |
| `WorkflowInstances` | 0 | 1 (the check's requisition, Completed) |
| Realm roles | 22 | 29, with `workflow-admin` |
| `.env` | with `GATEWAY_CALLER_PROOF_SECRET` | without it |
| Containers | 22 | 22, 0 unhealthy |

**Backup:** `/opt/hrm/backups/pre-plan43-20261106T140127Z` (700/600): `pg_dumpall.sql` 25.9 MB, `env.backup`, SHA256SUMS OK, 17 `CREATE DATABASE` lines (the 16 databases and `hrm_admin`).

**Deploys:** the first, of `c6faf0a92e6e`, took 156 s. The second stopped at `keycloak-reconcile` right after PostgreSQL restarted (`could not read the realm roles`): the tag stayed and the applications were down about 4 minutes. The same command then worked in 120 s, reconcile 0 changes, smoke 50 pass, 0 fail, 1 warn. This is the retry gap open since PLAN-40.

**Checks:**

1. Definitions carry `Status`, and the backfill matches the before-picture.
2. The health report lists 6 of 16 invalid, and the service stays healthy: `ID-CARD-ISSUANCE` and `IT-TICKET` as expected, and `HIRE-WIZARD`, `SEPARATION-WIZARD`, `TRANSFER-APPROVAL` and `DISCIPLINARY-APPROVAL` because their types are neither handled nor registered, as the plan's rule says.
3. The realm has `workflow-admin`.
4. Tour: `e2e-checker-mgmt` approved a requisition through both steps; a leave approval works (`AC_OPS_38`).
5. A user on `POST /api/hr-core/api/workflow-instances` gets 403 through the gateway.
6. No in-flight instance existed before the deploy.
7. Smoke with mandatory wiring green.
8. AcOps36 (36j excepted), AcInt37 server, PLAN-38, 39 and 40 server checks: 21 of 21, plus the 2 PLAN-43 checks.

The server keeps one approved requisition named «PLAN-43 server check». Changes and undo are in `docs/OPS_VPS_SINGLE_HOST.md` §10 and §16.

## 8. The parallel run

- **Mode:** normal. The first attempt, at 2026-11-05T19:39:47Z, stopped at precondition 1: primary clone, PR 37 open, no second clone. The owner chose to wait for PR 37.
- **Base:** `origin/main` 7480843e. No `rebase --onto`, no conflicts, and `main` did not move.
- **Lock waits:** none. `acceptance-stack` was held 11:46Z to 13:32Z, `demo-server` 14:01Z to 15:25Z.
- **`WAITING-PEER:`** 11 h 26 min, 19:45Z to 07:11Z. PR 37's first run went red at 21:21Z and stayed so until the owner was told at about 23:00Z, because a session that ends its turn does not watch its PR and my watcher fired only on merge or close.
- **Overlap with PLAN-42:** none, so the early start saved nothing.

`docs/PARALLEL_PLANS.md` records what this taught.

## 9. Deferred items

- **M6, for PLAN-45:** if the start times out after HR.Core committed, the assignment has no link, so its outcome is ignored and a stuck loss report blocks new ones. PLAN-45's retry with the same key closes it.
- **L2:** `WorkflowStarter` has no registered-type guard; the check is in `PolicyOrchestrator`, and no other caller uses a custody type.
- **L5:** publish is not tied to a hash of the reviewed content; a submitted definition cannot be edited, and `xmin` covers the race.
- **L6:** `WorkflowCode` is required on the wire, so the service's default is reached only by an empty string.
- **L7, for PLAN-45:** with no tenant, an instance lands in the national tenant and a retry can duplicate it.
- **L8:** the validator accepts catalog roles that `WorkflowStepAction` refuses, so a definition could name a role nobody reaches. PLAN-45 and PLAN-46 add the roles.
- **PLAN-44:** the unique index on the key. Two concurrent starts with one key can make two instances until then.

## 10. Defects for later plans

- `kc_body` in `reconcile.sh` returns the status of `rm`, so a failed step that uses it logs success. The new role step does not use it.
- `keycloak-reconcile` has no retry for a Keycloak that answers while PostgreSQL restarts.
- The demo seed writes requisition numbers without moving the counter, so the first real create answered 500. The check moved it past them on this server.
- A local `acceptance-job.ps1` run leaves `hrm-leastpriv` up.
- `scripts/acceptance-quarantine-flow.ps1` and other lab scripts need PowerShell 7, so those classes cannot pass on this machine.

## 11. Timing

Start 2026-11-06T07:12:24Z, to the end of P7 at 15:25:44Z: 8 h 13 of wall-clock. The merge time and the PR's CI wait go in the merge note.

| Stretch | Time |
|---|---|
| First attempt, stopped at the precondition | 2026-11-05 19:39Z, 6 min |
| Waiting for PR 37 (`WAITING-PEER:`) | 11 h 26 |
| P0 | 07:12Z to 07:21Z, 9 min |
| P1 | 07:21Z to 07:38Z, 17 min |
| Stopped by the owner | 07:38Z to 11:04Z, about 3 h 26 |
| P2 | 11:04Z to 11:23Z, 19 min |
| P3 | 11:23Z to 11:33Z, 10 min |
| P4 | 11:33Z to 11:44Z, 11 min |
| P5, with the review and its fixes | 11:44Z to 13:32Z, 1 h 48 |
| Local CI, twice | 13:32Z to 14:01Z, 29 min |
| P7, two builds and three deploy runs | 14:01Z to 15:25Z, 1 h 24 |

Active time from the start to the end of P7: about 4 h 47.

| Step | Time |
|---|---|
| Inventories | about 7 min, one subagent, beside the baseline |
| Images and lab stack | 778 s |
| Prod-like stack | 247 s without a build; 261 s up after a reset |
| leastpriv stack | 397 s |
| Whole job | 3473 s |
| Local CI | 1030 s |
| Server build | 1349 s and 1299 s |
| Deploy | 156 s and 120 s, plus 82 s failed |
| Server suites | 1226 s |

Actions minutes are added to the merge note, because they are not known yet.

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47  12:48
PLAN-39      13 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 11:05  False 8:52   8:53
PLAN-40      20 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 11:56  False 48:51  48:55
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08  27:16
PLAN-43      12 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 18:25  False 8:04   8:13
```

`scripts/plan-timing.ps1 -From 25 -To 43`, the last plans shown, local time. PLAN-42 is missing, as its report explains: the scope `s2s042` does not fit the script's pattern.

## 12. Readiness note

**PLAN-44 builds on:** `ExternalApprovalService` (the key lookup, which needs its unique index), `WorkflowInstanceActionService`, and `WorkflowInstance`'s new columns. `xmin` is on `WorkflowDefinition` only.

**PLAN-45 builds on:**

- `ExternalApprovalService` and `WorkflowTriggerService`, which it rebuilds into the trusted custody start, with the retry of M6 through `ActingUserScope`.
- `WorkflowPackSeeder.EnsureVersionAsync` to raise `ASSET-ASSIGN-APPROVAL` and `ASSET-LOSS-APPROVAL`, and `WorkflowGuardrails`, which it tightens.
- The exemption of `AssetLossReport`: lift it from the three settings files, the compose file, `DefaultExemptEntityTypes` and `docs/qa/workflow-step-role-exemptions.json`, after adding `admin-logistics` to `WorkflowStepAction`.
- AC-11, which it rewrites as a UI journey.

**Files both touch:** `WorkflowInstanceActionService.cs`, `WorkflowInstance.cs`, HR.Core migrations, `WorkflowPackSeeder.cs`, `tests/acceptance/ci-scope.json` and `ac-ownership.json`.

**PLAN-47,** if it runs beside either: the allow-list is empty, so a `TriggerWorkflow` rule never starts a registered type, and a published rule needs only its binding checked. It shares the same list files, and the stack lock.

**For every plan:**

- Never backfill a tenant table in a migration: use a start-up patch, and check the data on the server.
- A persona change needs `scripts/acceptance-reset.ps1` before the first start.
- Definition writes need the antiforgery token.
- A test that starts a custody type goes through `Start`, and its requester acts on no step.
- `main` is strict: a merge by another plan costs a full rerun.
