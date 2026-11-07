# PLAN-44 final report: workflow instance integrity under concurrency and failure

Branch `feature/plan-44-wf-instance-integrity`, from `origin/main` f8f5cd04 (PLAN-43 merged), normal mode, in the primary clone. Plan: [PLAN-44](../docs/acceptance-plans/PLAN-44-wf-instance-integrity.md). Progress with every stamp and undo: [plan44-progress.md](plan44-progress.md).

**Outcome:** two approvals of one step end with one decision, one signature and an Arabic conflict message. An entity and an idempotency key have at most one running instance, by unique indexes. A decision, its effect and its outbox row commit together. A reconciliation job finds outcomes that slipped, and the operator endpoint repairs them after a dry run. The gates and the inbox bar the same makers, the legacy path checks the step's role, and only the requester cancels. The demo server runs this code with row security still forced.

Five things the owner should read:

- **The plan's premise was wrong.** `ApproveAsync` and `RejectAsync` were not inside `OutboxUnitOfWork`: every save committed alone. This plan added the wrapper (section 3).
- **My first gateway fix did not work in a browser.** AC-WF-44d found that a two-step legacy request could never finish; the fix then missed the URL the form really posts, and the test passed on a hand-built URL. The independent review caught it. The test now posts the rendered form (section 3).
- **`7554cf42` was committed with an architecture guard red.** My command chained the commit after the tests. It was fixed in the next commit, before any push, and later commits were gated on the results (section 2).
- **`Ac29_e` failed twice locally,** a connection reset and then an upload stuck for 16 s, while the host was stalling, and passed on the third run. It touches nothing here, and no root cause beyond the host stall was proved (section 6).
- **Nothing needed resolving on the demo server:** 0 duplicates and 0 stuck outcomes before and after. The migration's resolving path is proved on PostgreSQL only, as a non-superuser owner (section 7).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | Baseline, inventories, server read-only picture; no Stop |
| P1 | `xmin`, conflict message, decision writes in one unit of work, the step the page showed |
| P2 | Two unique indexes, the migration, `WorkflowInstanceInsert`, inbox linkage, report script |
| P3 | Second repair category, reconciliation job, metrics |
| P4 | `WorkflowMakerRules`, the five gates, inbox, legacy role check, requester-only cancel, deletions, guard |
| P5 | 14 PostgreSQL proofs; AC-WF-44a..e; regressions |
| P6 | PLAN-66 merged during the run: `origin/main` merged at 973d6848, no conflict |
| P7 | Demo server on `973d68488f09`; every server check green |
| P8 | Review run before P7, so the server built once; docs, this report, the PR |

## 2. Commits

| Commit | Subject |
|---|---|
| 9b0b0790 | chore: baseline, with PLAN-43's merge note |
| 7554cf42 | feat: `xmin` and the conflict message. **Committed with `OutboxPublishOrderingArchitectureTests` red** |
| d6d1f4f1 | fix: the unit of work sits where the guard reads it |
| d4df6578 | feat: one running instance per entity and per key |
| d9926451 | feat: one transaction; reconciliation |
| fa92c468 | feat: one maker rule; the legacy path; deletions |
| 5f636710 | test: PostgreSQL proofs; AC-WF-44 written |
| ec5bdd1b | fix: the gateway admits the approver to a legacy step |
| 1fb4cebd | test: acceptance |
| 14daf4dd | fix: review findings |
| 973d6848 | merge: `origin/main` (PLAN-66); the tag on the server |
| afb0922b | test: demo server |

## 3. Decisions applied and deviations

D-WF-8 and P-1 to P-14 applied. Deviations, each with its reason:

- **The wrapper did not exist,** against the plan and P-4. Neither the service nor a controller used `OutboxUnitOfWork`. All checks stay before it: `DecideAsync` stages the appeal's outcome on a tracked entity, and the wrapper's last save would have committed it on a refusal.
- **`FORCE` is lifted on eight tables, not one.** The entity tables are as invisible to the migrator as `WorkflowInstances`, so lifting it on one would have found no link and kept the newest of every group. Lifted and restored inside the migration's transaction; no guard forbade it.
- **The review ran before P7, not in P8,** so that P-13's single build was on the final code.
- **Two server builds' worth of time, one image set.** The first build's session was reset before its push. The same committed script ran again detached.
- **Two backups:** the first was ten hours old when the deploy came.
- **`origin/main` moved** after P5: P6 merged PLAN-66. No service code changed, so acceptance was not repeated for it; the unit suite was, and the PR run covers the rest.
- **The refusal in AC-WF-44e is the existing `CreatorMessage`,** not the plan's quoted text: the plan's own design table asks for it, and AC-HCM-25 pins it.
- **AC-WF-44a does not assert that both responses point at the instance.** A submission that reads after the first committed is refused because the request is no longer a draft. The invariant, one running instance, is asserted. Both pointing at it is proved in `Wf044InstanceIntegrityPostgresTests`.
- **`[Collection("PgRabbit")]`, not `IntegrationSerial`:** the fault test needs a real outbox, as PLAN-43's did.
- **`WorkflowPlan03Seeder`** was a third, live seeder the plan did not list. Its method moved into `WorkflowPackSeeder`, so the guard has no exception for it.
- **Additions the plan did not ask for:**
  - forms send `TargetStepLevel`, so a stale page gets the conflict message instead of acting on the next step; the API answers 409;
  - a change that cannot apply refuses the last approval (`WorkflowOutcomeRefusedException`): the handler ignored that failure and nothing re-applied it;
  - the gateway applies `HrCoreApprove` to `Approvals/Approve` and `Reject`, the controller's policy;
  - `CompetitionLifecycleService.CancelAsync` cancels the running approvals it used to leave behind;
  - competition, results and appeal handlers check the link;
  - competition and results starts commit with their link;
  - nested transactions in `EstablishmentChangeApplier` and `CodeGeneratorService` join the decision's;
  - the legacy inbox offers an admin a step only under `AdminBypass`.
- **Tests changed because the behaviour changed on purpose** (none is an AC-HCM-25 assertion or a parity case):
  - `Hcm025ReviewFixTests` F1 and F2: a start beside a running instance is now refused, and a second running instance cannot exist;
  - `ApprovalWorkflowTests.Cancel_PendingRequest_SetsCancelled`: the fixture is now the requester;
  - `Hcm032RecruitmentMetricsTests`: the fixture links its instance;
  - `Msg040ClassAPublishOrderTests`: defines its definition inline, the seeder being gone;
  - `Wf001WorkflowEngineTests` lost the two bridge tests, and `wf001-workflow-validate.ps1` its check for the deleted bridge.
- **Mistakes of mine, corrected:** the commit in section 2; a release of `acceptance-stack` that failed for want of `-Plan` while the progress file said it had happened; a watcher whose `pgrep` matched itself, so it never saw the build end.

## 4. The inventories

Details in [plan44-baseline.md](plan44-baseline.md).

| Item | Acceptance | Server before | Server after |
|---|---|---|---|
| Running duplicates | no data before; 0 after | 0 | 0 |
| Key duplicates | 0 after | 0 | 0 |
| Finished instance, entity pending | 0 after | 0 | 0 |
| Owner of `WorkflowInstances` | `hrm_ci` | `hr_core_migrator` | `hr_core_migrator` |
| Enabled and forced | yes | yes | yes, all eight tables |

- **Status writers:** the two decisions, external start and cancel, the starter and its four callers, repair, and four cancels that bypass the engine.
- **Maker scenarios (P-5):** no scenario had a submitter or requester succeed on a step. No persona changed. AC-HCM-25 needed no change.

## 5. The guards and their negative tests

| Guard | Proves | Its negative |
|---|---|---|
| `Data001ConcurrencyArchitectureTests` | `xmin` on `WorkflowInstance` | a new line |
| `Wf044DefinitionSeedingArchitectureTests` | only the pack and the governed authoring create a definition | a theory on sample source; each allowed file must still match |
| `OutboxPublishOrderingArchitectureTests` | unchanged; it failed twice during this plan and was satisfied, not relaxed | its own |
| `Hcm025PanelServerParityTests` | existing 84 cases unchanged, plus makers for all five types and inbox parity | each maker is a case |
| Migration test | the owner sees 0 rows before the migration | refuses an undecidable group; fails for a non-owner |
| `GatewayPathAuthorizationMiddlewareTests` | both forms of the legacy path; `Cancel` stays `HrCoreWrite` | `ApproveAll` stays `HrCoreWrite` |

## 6. Results

| Run | Result |
|---|---|
| Build `-warnaserror` | 0 warnings, 0 errors |
| Unit on the merge | 5244 of 5244 |
| `Wf044InstanceIntegrityPostgresTests` | 14 of 14; each race forced by a barrier |
| Wider integration (Wf043, Hcm025, Data001, Msg040, Ten002, Est002, SoD) | 102 of 102 |
| After the review (Wf044, Wf043, Hcm031) | 38 passed, 1 skipped |
| Job, `-Profile Both`, 17 classes (ec5bdd1b) | prod-like all pass; lab 47 of 48 (`Ac29_e`) |
| `AcHcm29` alone | failed, then 3 of 3 |
| Job after the review, 10 classes (14daf4dd) | PASS: AC-WF-44 5 of 5, guard green |
| Local CI on afb0922b | 91 steps passed, 0 failed, 1329 s: unit 5244, integration 366, Testcontainers 162, all 0 failed and 0 skipped |
| Pre-push check | OK, 793 s |

The 1 skip of the filtered run was not identified; it did not recur in local CI, which skipped nothing. AC-HCM-29 was not rerun after the review, because the review's fixes do not touch it; the PR run covers it.

**`SensitiveDataAuditTests.Log_with_anonymous_context_does_not_throw`** failed once in a parallel run and passed alone and in every later full run. It is not touched here; its cause was not looked for.

**Evidence of record:** the PR run's artifact, linked in the merge note. No evidence is committed.

## 7. The server

| | Before | After |
|---|---|---|
| Tag | `3bd9ee92bcf5` | `973d68488f09` (previous `3bd9ee92bcf5`) |
| Indexes | none | both |
| Row security | forced | forced, eight tables |
| Instances | 2 Completed | those 2, plus the checks' requisitions |
| Containers | 22 | 22, 0 unhealthy |

**Backups:** `/opt/hrm/backups/pre-plan44-20261106T221735Z` and `pre-plan44-20261107T083208Z` (700/600): `pg_dumpall.sql` 26 MB, `env.backup`, SHA256SUMS OK, 17 `CREATE DATABASE` lines (the 16 databases and `hrm_admin`).

**Deploy:** 147 s, with no `keycloak-reconcile` failure. Smoke 50 pass, 0 fail, 1 warn. Downtime was not measured separately.

**Checks:**

1. Migration applied: both indexes, all eight tables forced.
2. Duplicates report: zero. There was nothing to resolve.
3. Job: `1 tenant(s), 0 item(s), auto-apply False`; 7 metric series at 0; the dry run listed 0 items, so nothing was applied.
4. Tour: `e2e-checker-mgmt` approved requisitions; leave approval (`AC_OPS_38`).
5. Two sessions on one step: one decision, one signature, one conflict message, the counter up by one.
6. Smoke with mandatory wiring green.
7. AcOps36 (36j excepted), AcInt37, PLAN-38 to 43 server checks: 21 of 21, 997 s. AC_WF_44_vps: 3 of 3.

The server keeps approved requisitions named «PLAN-44 server check» and «PLAN-43 server check». Changes and undo are in `docs/OPS_VPS_SINGLE_HOST.md`.

**Rollback safety:** `xmin` adds no column. On the previous tag a concurrent duplicate insert fails with a unique violation, and so does a sequential start beside a running instance that it cannot reuse, where it used to make a second instance. Both are the index doing its job, shown to the user as an error. Rollback itself was not exercised.

## 8. The parallel run

- **Mode:** normal, primary clone, base f8f5cd04. PLAN-43 was already merged.
- **PLAN-66** ran beside this in `HRManagement-b`, held `acceptance-stack` until about 19:09Z and ran local CI from about 19:07Z. P-2 was honoured: Testcontainers waited for it. It merged as PR 39 during P5 and P6 merged it.
- **Lock takeover:** `demo-server` expired during the outage and was retaken, my own.
- **Owner's file:** the untracked `docs/acceptance-plans/execution/PLAN-66-AGENT-PROMPT.md` differed from the version PLAN-66 merged and blocked the merge. It was moved unchanged to `artifacts/plan44/PLAN-66-AGENT-PROMPT.local-untracked-copy.md`.

## 9. Deferred items

- **L3, a cancel and a decision can deadlock:** they lock the instance and the entity in opposite orders. Both roll back; the loser gets a 500 with no data damage. Ordering the writes, or mapping 40P01 to the conflict message, closes it.
- **An upheld objection that slipped** is left to the operator, because the recompute lives in `CompetitionObjectionService`.
- **A second submission after the first committed** gets a status refusal, not the winner's instance.
- **`AutoApply` was not exercised** on a real server, and stays false.

## 10. Defects for later plans

- The Review page's forms carry no route id, which is why the gateway rule needs both path forms.
- `RecruitmentOfferExpirer` and other jobs read under `IgnoreTenantFilterForSystem` with no tenant. Under forced row security they may see no row, as PLAN-43's backfill did. Not checked here; the new job avoids it by setting the tenant.
- `build-on-host.ps1` ties the build to one ssh session: a reset loses the push after a full build. Running it detached survived the outage.
- The demo seed's requisition counter, the `kc_body` status and the `keycloak-reconcile` retry remain from PLAN-43.

## 11. Timing

Start 2026-11-06T18:11:31Z, to the end of P7 at 2026-11-07T08:53Z: 14 h 42 of wall-clock, about 9 h 56 of it the host's outage. The merge time, the PR's CI wait and Actions minutes go in the merge note.

| Stretch | Time |
|---|---|
| P0 to P4 | 18:11Z to 19:15Z, 1 h 4 |
| Waiting for PLAN-66's Docker (`P-2`) | about 19:22Z to 19:30Z, inside the writing of AC-WF-44 |
| P5, with the review and its reruns | 19:15Z to 22:13Z, 2 h 58 |
| P6 | 22:14Z, minutes |
| Host outage (`WAITING`) | 22:26Z to 08:22Z, 9 h 56 |
| P7 after the outage | 08:22Z to 08:53Z, 31 min |

Active time to the end of P7: about 4 h 46.

| Step | Time |
|---|---|
| Inventories | 7 min, one subagent |
| Prod-like job, first | 1299 s, stack 1020 s |
| Both profiles, 17 classes | 2925 s |
| Both profiles, 10 classes | 1792 s |
| Server build | 414 s to the reset; rerun about 8 min on warm layers |
| Deploy | 147 s |
| Server suites | 997 s and 66 s |

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-40      20 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 11:56  False 48:51  48:55    
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08  27:16    
PLAN-43      15 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 21:06  False 10:45  10:54    
PLAN-44      11 2026-11-06 21:11 2026-11-06 21:20 2026-11-07 11:53  False 14:33  14:42
```

`scripts/plan-timing.ps1 -From 25 -To 44`, the last plans shown, local time.

## 12. Readiness note

**PLAN-45 builds on:** `WorkflowInstanceInsert`, which every instance insert uses; `ExternalApprovalService`, where a retry with the same key now returns the same instance under a race; the conflict message and 409; `WorkflowOutcomeRefusedException`.

**PLAN-46 builds on:** the repair categories, where external types are deliberately absent; `WorkflowMakerRules`; the `Workflow:Maker` setting.

**Files they share with this plan:** `WorkflowInstanceActionService.cs`, `ExternalApprovalService.cs`, HR.Core migrations, `WorkflowPackSeeder.cs` (it now also holds the multi-step custody method), `ci-scope.json`, `ac-ownership.json`.

**For every plan:**

- An `ApproveAsync` caller must not expect a refusal to save what it staged.
- A new handler must check state and link, and run inside the decision's transaction: no `BeginTransaction` without joining.
- A new start goes through `WorkflowStarter` or `WorkflowInstanceInsert`, and handles `WorkflowInstanceRunningException`.
- A test that needs a second running instance must use a finished one: SQLite enforces the index too.
- A new tenant job sets `TenantIdFlow` before its scope opens a connection.
- An acceptance test posts the URL the page renders, not one built by hand.
