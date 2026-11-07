# PLAN-45 final report: closing the custody and loss approval loop

Branch `feature/plan-45-wf-asset-custody-approval-loop`, from `origin/main` aeb6d716, in the main folder, with no other plan beside it. Plan: [PLAN-45](../docs/acceptance-plans/PLAN-45-wf-asset-custody-approval-loop.md). Progress with every stamp and undo: [plan45-progress.md](plan45-progress.md). Baseline: [plan45-baseline.md](plan45-baseline.md).

**Outcome:** a custody or loss request is saved first and waits visibly until its approval exists, so no pending request has no instance. If HR.Core is down, a job starts it later in the right tenant, by the same key. The requester or a logistics officer can withdraw it, and the asset is free. The chain is `admin-logistics` then `hr-management-approver`, by different people, with the role enforced. Both approvers work from the work inbox on a generic page. The outcome reaches AdminLogistics. The demo server runs this code, and demo users have walked the chain on it.

Five things the owner should read:

- **The review caught a bug of mine.** I changed the separation compensation filter to `Status == Returned`, but the saga closes rows by `ReturnedAt` and leaves their status, so compensation stopped reopening what it had closed. Nothing had tested that path. It is fixed, with a test (section 3).
- **The job finds its tenants in `AuditLogs`.** AdminLogistics has no tenant table, and no service read returns one. The audit table is outside row security and gets a row in the same save as each request, so this needs no new trust. It depends on that table's rows not being purged while a request waits (section 4).
- **The two demo users could not sign in at first.** The live realm's user profile does not declare `tenant_id`, so Keycloak silently dropped the attribute. `scripts/vps/add-demo-user.sh` now allows it for the write and puts the profile back (section 7).
- **The first run of the server suites hit ssh connection errors:** 9 failed and 6 skipped. None was an assertion. Each one was rerun and passed (section 7).
- **A rollback leaves the new definitions published.** On the previous tag a new custody request cannot pass step 1. Nothing crashes and no data is lost, and it clears on roll-forward (section 7).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | Preconditions, baseline, two read-only inventories, server read-only picture; no Stop |
| P1 | The states, the fields, the migration, `AssetApprovalStarter`, `AssetApprovalLoop`, the job, the metrics, the asset page |
| P2 | Both codes on the D-WF-2 chain, guardrails, policies, `ExternalApprovalGate`, the exemption lifted for `AssetLossReport` |
| P3 | The generic approval page, the gateway rule, external types in the inbox, `WorkInboxAudience` |
| P4 | PLAN-44's L3 closed, the repair script, 8 PostgreSQL proofs, AC-11, AC-33 and AC-WF-43e/f rewritten, AC-WF-45 written |
| P5 | Acceptance green, one independent review, fixes, CI scope and ownership |
| P6 | `origin/main` did not move: nothing to do |
| P7 | Demo server on `9560b03e51a3`, demo users, every server check green |
| P8 | Docs, local CI 91 of 91, this report, the PR |

## 2. Commits

| Commit | Subject |
|---|---|
| 53f5fd34 | PLAN-44's merge note |
| ab9cda16 | PLAN-66's closing notes and this plan's prompt |
| 7f5ff9a7 | chore: baseline and inventories |
| bd36cf74 | feat: the loop in AdminLogistics |
| 5348fb37 | feat: the chain, enforced |
| cb5f09ac | feat: the generic page and the inbox |
| 2b64cbaa | test: AC-11, AC-33, the repair script, PostgreSQL proofs |
| ad40aca9 | test: acceptance green, review fixes, scope and ownership |
| 9560b03e | docs: journey, inbox, SoD, status lines (the tag on the server) |
| b5b7853a | chore: no byte order mark in the migration; the demo-user script |
| 8957cc32 | fix: a demo user keeps its tenant; the server check picks an HR.Core employee |

Only a byte order mark, a script and a test changed after the tag, so no image differs from `main`'s code.

## 3. Decisions applied and deviations

D-WF-2, D-IDA-4 and P-1 to P-15 applied. Deviations, each with its reason:

- **Only `AssetLossReport` left the exemption list,** as this prompt says, against the plan's `[]`. The four PLAN-46 types stay.
- **`WorkflowTriggerService` is gone.** `AssetApprovalStarter` replaces it, and `AssetApprovalLoop` holds the start, the retry and the withdrawal.
- **The unique index on loss reports changed** to `"Status" IN (0, 4)`. The plan did not mention it: without this, a report in `ApprovalStarting` would allow a second one.
- **L3 is closed by mapping `40P01`,** in the decisions, the cancels, `SaveOrConflictAsync` and `ConcurrencyConflictMiddleware`, not by reordering locks. P-6 allows either; mapping covers every path at once.
- **The repair report goes to `artifacts/plan45/`,** as P-10 says, not to the plan's `reports/` path.
- **`-RestartOnLatest` edits two databases by SQL,** HR.Core first. It has no atomic step between them, and it is never for the demo server.
- **AC-11 keeps its Enforce check on agency B** and runs the journey in agency A: B has one logistics persona. On this stack AdminLogistics decides by itself, so AC-10's deny version does not reach the journey.
- **AC-WF-43e and 43f were rewritten:** step 1 no longer belongs to `hr-core`, and a logistics-only user acts on the generic page, because `WorkflowInstancesController` keeps `HrCoreReadOnly`.
- **AC-WF-45h reruns the shared journeys** on the prod-like stack, instead of reading AC-11's and AC-33's results.
- **The review ran beside the first acceptance run,** not after it, to save time. Its fixes went through a rebuilt stack before the single server build.
- **The P0 baseline commit was redone** before any push: it had swept in a staged deletion.
- **The note branches were deleted with `-D`:** `-d` refuses a cherry-picked branch. Their content was verified first; `5a3ce027` was already on `main`, so its pick was empty.

**Additions the plan did not ask for:**

- `RequestedByName`, so a start by the job still names the requester.
- The starter sends `CreatedBy` as the acting user, so a retry by a colleague does not change the requester.
- Withdrawing a request in `ApprovalStarting` first looks up the key, to cancel an instance whose answer was lost.
- A branch-level holder of a step role reads the page inside their unit only.
- A short menu for a logistics-only user.
- `IdempotencyRecords` in `admin_db`, without which `[Idempotent]` does nothing at all.
- `ApplyApproved` refuses a request a transfer or separation closed.

**Review fixes:**

| Finding | Outcome |
|---|---|
| High: separation compensation inverted, by my P1 change | Confirmed against the code. Fixed, with a test: it reopens Active, PendingApproval and ApprovalStarting rows, and only an Active row returns its asset to InUse |
| Medium: the new log line would fail the CodeQL gate | Fixed: `Path.Value` |
| Low: an approval could revive a request a transfer closed | Fixed for the approval path. The rest is in section 8 |
| Low: rollback covers statuses, not definitions | Recorded in the baseline, the OPS guide and section 7 |
| Low: a withdrawal with HR.Core failing can leave an instance running | Deferred, section 8 |
| Low: the manifest alone creates no user on a live realm | Already planned; `add-demo-user.sh` |
| Low: `[Idempotent]` does nothing for a browser | Noted, section 8. Double clicks are stopped by the unique indexes |
| Not checked by the review: the audit path under row security | The row-security test now seeds as the app role through the real interceptors |

A refuter was not used: the one high finding was checked directly against `PartyAckConsumers.cs`.

## 4. The inventories

- **Pending requests:** none anywhere before this plan. The server had 300 `Active` assignments, no loss report, and 200 assets, none privileged. The acceptance seed has one `Active` assignment. The repair found 0 on the server, dry and applied.
- **Definitions, server, before:** `ASSET-ASSIGN-APPROVAL` v1 (`hr-core`, then `hr-management-approver`) and `ASSET-LOSS-APPROVAL` v1 (`admin-logistics`). After: both v1 Retired, both v2 Published on the chain with distinct approvers.
- **Tenant source:** `AuditLogs` of `admin_db`, narrowed to the two entity types, with a new index on `(EntityType, TenantId)`. Each `<svc>_app` role connects to its own database only, so HR.Core's `Tenants` table is out of reach, and no existing read returns it. The job relies on those audit rows staying, so a future retention job on this table must keep them while a request waits.
- **Runtime roles:** `admin_app` in production and in `hrm-leastpriv`. The demo server and the main acceptance stack connect as the administrator, so row security does not bind the app there. The proof uses a real non-superuser role.
- **Policies:** `admin-logistics` joined `WorkflowStepAction` and `HrCoreWorkInbox` only. `/ui/hr-core/WorkflowApprovals` sits before the mutating block, because `IsHrCoreWorkflowMvcAction` would otherwise send its POSTs to `HrCoreBranchOperation`.

The full inventories are in [plan45-baseline.md](plan45-baseline.md).

## 5. The guards and their negative tests

| Guard | Proves | Its negative |
|---|---|---|
| `Wf045NoPendingWithoutInstanceArchitectureTests` | only `LinkPending` writes `PendingApproval`, and only the rules and the loop write the instance | sample lines prove the pattern sees a write, a ternary and a property default, and leaves a comparison alone |
| `The_logistics_role_reaches_two_policies_of_hr_core_and_no_other` | `admin-logistics` in exactly two policies | five policies refused |
| `The_gateway_refuses_the_logistics_role_on_the_rest_of_hr_core` | 403 at the gateway | `AC-WF-45g` and the server check on `/ui/hr-core/Employees` |
| `Wf045WorkflowApprovalsControllerTests` | who reads, who decides, antiforgery, 404 for other types | `Holding_the_inbox_opens_no_instance_by_its_id` |
| `Wf043SodRulesTests` | settings, code and registry agree on open exemptions | a type added in one place fails |
| `Under_forced_row_security_the_job_finds_both_tenants...` | the job as a non-superuser, two tenants | a session with no tenant finds 0 rows |

## 6. Results

| Run | Result |
|---|---|
| Build `-warnaserror` | 0 warnings, 0 errors |
| `has-pending-model-changes`, AdminLogistics and HR.Core | none |
| Local CI on 8957cc32 | 91 steps passed, 0 failed, 1629 s: unit 5373, integration 362, Testcontainers 169 |
| `Wf045AssetApprovalPostgresTests` | 8 of 8 |
| AC-WF-45a..h | 8 of 8, 49 s on the final images |
| Lab: Ac11, Ac32, Ac33, AcWf23, AcEst24, AcHcm28 | 16 of 16 |
| Prod-like: AcWf43, AcMsg40, on the final images | 6 and 8 |
| Prod-like, before the review fixes: AcHcm25, AcWf44, AcOps39, AcS2s42, AcInt37 | 8, 5, 8, 9, and 6 of 8 |
| `wf043-validate`, `int037-wiring-check` | PASS |

`Ac37b` and `Ac37e` are Blocked in a `-Classes` run: they need the journey stage. This is recorded from PLAN-42 and is not a regression; the PR run covers them. The prod-like classes in the last row were not repeated on the final images, because the fixes do not touch them. The PR run covers them too.

**Two local CI failures, neither in the code's behaviour:** EF wrote a byte order mark into the migration file, which the format gate refused. Then `Ops038` could not download its RabbitMQ plugin from GitHub; the rerun passed.

**Evidence of record:** the PR run's artifact, linked in the merge note. No evidence is committed.

## 7. The server

| | Before | After |
|---|---|---|
| Tag | `973d68488f09` | `9560b03e51a3` (previous `973d68488f09`) |
| Definitions of custody | 2 Published v1 | 2 Published v2, 2 Retired v1 |
| Exemption | `AssetLossReport` exempt | not exempt in the running settings |
| Realm users with `admin-logistics` | none | `e2e-logistics-maker`, `e2e-logistics-checker` |
| `admin_db` | no `IdempotencyRecords` | migration applied, table under forced row security |
| Containers | 22 | 22, 0 unhealthy |

**Backup:** `/opt/hrm/backups/pre-plan45-20261107T155048Z` (700/600): `pg_dumpall.sql` 26.6 MB, `env.backup`, SHA256SUMS OK, 17 `CREATE DATABASE` lines.

**Build and deploy:** one build, detached, 1280 s, 17 of 17 images. Deploy 150 s: pull 1, up 81, bootstrap 38, reconcile 7, healthy 13. No `keycloak-reconcile` failure. Downtime was not measured separately; it falls inside `up`. Smoke with mandatory wiring: 50 pass, 0 fail, 1 warn.

**Demo users for the owner's QA guide** (usernames only, the demo password as the other demo users):

| Step | User |
|---|---|
| Requester | `e2e-logistics-maker` |
| Step 1 | `e2e-logistics-checker` |
| Step 2 | `e2e-checker-mgmt` |

**Checks:**

1. Migration applied; definitions and running settings as above.
2. Repair: dry and `-Apply` both found 0 items. There was nothing to move.
3. A real custody approval: requester, checker and manager, from the inbox, ended `Active`.
4. A real loss: `Lost`.
5. A withdrawal: `Withdrawn`, the instance `Cancelled`, the asset free.
6. The requester sees no approve form. The checker gets 403 on `/ui/hr-core/Employees`.
7. The job's line, `1 tenant(s), 0 linked, 0 waiting, 0 need intervention`, and both metrics are present.
8. Smoke green.
9. AcOps36 (36j excepted), AcInt37 and the PLAN-38 to 44 server checks: all pass. On the first run 12 passed, 9 failed and 6 skipped, all from ssh errors during that run (`connection abort`, `permission denied` on port 22). The cause was not proved; local CI was running beside them. The rerun passed 13 of 13 and 6 of 6.

The server keeps assets named «PLAN-45 server check»: each run of the journey leaves one held, one lost and one free. Two runs were made, and one more partly ran. Changes and undo are in `docs/OPS_VPS_SINGLE_HOST.md`.

**Rollback safety:** new columns are nullable or defaulted, and `xmin` adds no column. On the previous tag:

- an `ApprovalStarting` row shows its number and holds the asset with no action;
- a `Withdrawn` row is closed;
- the definitions stay on the chain, and that tag has no `admin-logistics` in `WorkflowStepAction`, so a new custody request stops at step 1, with no withdrawal, until the roll-forward.

Rollback itself was not exercised.

## 8. Deferred items

- **A withdrawal can leave an instance running.** If a start committed, its answer was lost, and HR.Core fails again at the withdrawal, the request is Withdrawn with no link and the instance stays in the inbox. A decision on it changes nothing. A sweep by key would close it.
- **A waiting request closed by a transfer or a separation** keeps its status with `ReturnedAt` set. An approval is now refused, but a row in `ApprovalStarting` stays as it is and its instance, if any, stays in the inbox. This belongs with PLAN-62 and IDA-D.
- **`[Idempotent]` does nothing for a browser,** which sends no key. Double clicks are stopped by the unique indexes.
- **PLAN-43's L7 is closed for the job,** which always sends the request's tenant. HR.Core itself still falls back to the national tenant for a start with no tenant.

**Closed here:** PLAN-43's M6 and L8 (for this role), PLAN-44's L3.

## 9. Defects for later plans

- The live realm's user profile lacks the manifest's attributes (`tenant_id`, `employee_id`, `scope_org_unit_id`), so an admin cannot set them. `keycloak-reconcile` does not align it.
- `EmployeeOrgUnitSnapshots` is empty on the demo server, so every demo custody request has no unit, and a branch-level approver could not act on it.
- Seeded demo assets are not privileged, so a seeded asset's loss needs no approval. The server check creates its own.
- `AssetApprovalRules.RequiresApproval` (privileged only) and the shim (privileged or above 10,000) differ, so loss and custody ask for approval on different assets. This is unchanged from before, and D-WF-3 is the place for it.
- `TransferExecutedConsumer` and `SeparationPartyAckConsumer` close a row and leave its status.
- A run of `AcMsg40VpsChecks` writes a replay report under `reports/`.
- The items of PLAN-43 and PLAN-44 remain: `kc_body`, the reconcile retry, the demo requisition counter.

## 10. Timing

Start 2026-11-07T13:55:29Z, to the end of P7 at about 16:55Z: 3 h of wall-clock. The merge time, the wait for CI and Actions minutes go in the merge note.

| Stretch | Time |
|---|---|
| P0 | 13:55Z to 14:06Z, 11 min |
| P1 | 14:06Z to 14:17Z, 11 min |
| P2 | 14:17Z to 14:27Z, 10 min |
| P3 | 14:27Z to 14:31Z, 4 min |
| P4 | 14:31Z to 14:47Z, 16 min |
| P5, with the review and its fixes | 14:47Z to 15:50Z, 63 min |
| P7, with local CI beside it | 15:50Z to 16:55Z, 65 min |

| Step | Time |
|---|---|
| Inventories | about 8 min, two subagents |
| Independent review | 18 min, one subagent, beside acceptance |
| Prod-like stack with images | 897 s; without a build 264 s and 269 s |
| Lab stack with images | 905 s |
| Local CI | 1629 s |
| Server build | 1280 s |
| Deploy | 150 s; 218 s with smoke |
| Server suites | 8 min 42 s, then 14 min 20 s and 3 min 44 s for the reruns |

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-41      17 2026-11-04 11:48 2026-11-04 11:56 2026-11-05 15:04  False 27:08  27:16
PLAN-43      14 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 19:03  False 8:42   8:50
PLAN-44      14 2026-11-06 21:11 2026-11-06 21:20 2026-11-07 16:25  False 19:05  19:14
PLAN-45      10 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 19:27  False 2:31   2:32
```

`scripts/plan-timing.ps1 -From 25 -To 45`, the last plans shown, local time, before the final commits.

## 11. Readiness note

**PLAN-46 reuses:**

- the generic page: add a type to `ExternalApprovalTypes`, and the page, the inbox and the gate follow;
- `WorkInboxAudience`: its roles come from `AudienceRoles`;
- `ExternalApprovalGate`, one per registered type;
- the pattern of `AssetApprovalStarter`, `AssetApprovalLoop` and the job, including `AuditLogs` as a tenant source where a service has no tenant table;
- the exemption mechanism: close a row in `docs/qa/workflow-step-role-exemptions.json`, and remove the type from the three settings files, the compose file and `DefaultExemptEntityTypes`;
- `WorkflowApprovalDetailsViewModel.FactLabels`, for the labels of its facts.

**Files it shares with this plan:** `ExternalApprovalTypes.cs`, `WorkflowInboxQueryService.cs`, `WorkflowDeepLinkResolver.cs`, `WorkflowPackSeeder.cs`, `WorkflowGuardrails.cs`, `HrManagementAuthorizationExtensions.cs`, `ci-scope.json`, `ac-ownership.json`.

**For every plan:**

- A user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
- An EF migration file may carry a byte order mark, which the format gate refuses.
- A role from outside HR.Core acts on the generic page, not on `api/workflow-instances`: that controller keeps `HrCoreReadOnly`.
- Server suites run better with nothing heavy beside them.
