# PLAN-46 final report: cross-service approvals

The shared contract is in place. An ID card is issued, and a disciplinary action reaches payroll, only after an approval in HR.Core's engine by someone other than the requester. Separation is ready on HR.Core's side.

- **Branch:** `feature/plan-46-wf-cross-service-approvals`, merged through `release/plan-46`.
- **Demo server:** runs `dbf53280121d`.
- **Merge:** the progress file (`reports/plan46-progress.md`) records the PR, the checks and the merge commit, in its merge note.

## 1. Summary per phase

| Phase | What it did |
|-------|-------------|
| P0 | Preconditions met. Branch, PLAN-45's merge note and the prompt committed. Baseline in `reports/plan46-baseline.md`. Two read-only inventories. Server read-only counts. |
| P1 | The contract in `ServiceDefaults/Workflow/CrossService/` and `MessageContracts/Events/`: `IWorkflowApprovalClient`, `HttpWorkflowApprovalClient`, results, error codes with Arabic messages, `IWorkflowOutcomeDecided` and `WorkflowStepDecisionDto`, `WorkflowOutcomeGuard`, `WorkflowApprovalSweepJob<TStore>` with `IWorkflowApprovalOwnerStore`, and `FakeWorkflowApprovalClient` in TestSupport. |
| P2 | HR.Core's side, in one row each below. |
| P3 | The card, in one row each below. |
| P4 | Discipline, in one row each below. |
| P5 | PostgreSQL tests, acceptance on both profiles, one independent review (fixes with tests), CI scope and ownership. |
| P6 | `origin/main` did not move (`c8252dc6`): nothing to merge. |
| P7 | Server: backup, one build (after freeing a full disk), deploy (`keycloak-reconcile` retried once), three demo users, PLAN-36..46 server checks 31/31. |
| P8 | Docs, local CI, pre-push check, this report, release branch, PR, merge. |

**P2, HR.Core's side:**
- Three registry entries: `CardRequest`, `DisciplinaryAction` and `SeparationRecord`, all `Generic`.
- `ExternalOutcomePublisher`, staged in the decision's transaction (approve, reject, withdraw).
- `GET api/workflow-instances/{id}/external-status`, for the owner only.
- Guardrails for the three types.
- `idcard` and `compliance` reach `WorkflowStepAction`, and `idcard` reaches `HrCoreWorkInbox`.
- All exemptions lifted (`ExemptEntityTypes = []`; four registry rows closed).
- `calls[]` in the wiring matrix for the three services. The prod-like compose sets `IdCard__Approval__AutoSubmitOnHire` and the sweep intervals (1 and 2 minutes).
- `EnsureCrossServiceChainsAsync` raises `ID-CARD-ISSUANCE` and `DISCIPLINARY-APPROVAL` to v2.
- Four orphans retired.
- The guard finds consumers by reflection.

**P3, the card:**
- `PendingApproval` and `Approved` added at the end of `CardRequestStatus`.
- The new fields and `xmin`, migration `WF046_CardRequestApproval`.
- `CardRequestApprovalService`, `CardRequestApprovalOutcomeConsumer` and `CardRequestApprovalStore`, with the sweep.
- `ProcessRequest` and `IssuedCards/Create` issue only from an approved request.
- `Renew` opens a replacement request.
- The request pages: status, buttons per state, the approval link.
- Persona `a-idcard-2`.

**P4, discipline:**
- `DisciplinaryApprovalStatus`, the fields and `xmin`.
- Migration `WF046_DisciplinaryApproval`: the existing rows become approved-before-engine under forced row security.
- `Create` saves a draft, then submits.
- Only `DisciplinaryApprovalOutcomeConsumer` publishes `IDisciplinaryActionTaken`, with the consumer outbox and a message id taken from the action.
- `Edit` works only in `Draft` and `Rejected`; appeals only on `Approved`.

## 2. Commits

| Commit | Subject |
|--------|---------|
| `0ff98f7e` | docs(wf046): the PLAN-46 execution prompt |
| `b04777ee` | docs(wf045): merge note for PR 43 (cherry-picked) |
| `edf9dd20` | chore(wf046): baseline, ... |
| `6dfbdb74` | feat(wf046): one cross-service approval contract ... |
| `1a90f5a9` | feat(wf046): HR.Core registers the card, discipline and separation approvals ... |
| `dc98fe0a` | feat(wf046): an ID card is issued only from a request someone other than the requester approved ... |
| `7de879d3` | feat(wf046): a disciplinary action reaches payroll only after compliance and the authority approve it ... |
| `b23e8ea7` | test(wf046): AC-WF-46a..j pass on the prod-like stack ... review fixes; CI scope and ownership updated |
| `dbf53280` | test(wf046): server checks ... and the demo users the chains need (the server's release tag) |
| `8445c893` | test(wf046): demo server on cross-service approvals ... |
| (P8) | docs(wf046): docs, final report |

## 3. Decisions applied and deviations

The owner's decisions apply as written: D-WF-1, D-WF-1/D-ER-2 (the engine), D-SEP-4 (registration only), `AutoSubmitOnHire=false`, the existing records, and the sweep at 10/15 minutes (1/2 on the acceptance stack). The orphans are retired.

Deviations:

1. **`WorkflowApprovalStatusDto`** gets two optional last fields, `Reason` and `CorrelationId`. The sweep needs them to apply exactly what the event would have.
2. **`AlreadyActive`** keeps HR.Core's meaning: the instance is running. A cancel of a finished instance answers `Success=false, AlreadyTerminal=true`, so a check on `Success` can never return a record to draft.
3. **`WorkflowApprovalSubmission`** is a shared helper beyond the plan. It holds rules 2 to 5 for every owner, and PLAN-60 reuses it. A key whose instance was withdrawn before the record pointed at it is spent, and the next number is used.
4. **No consumer endpoint definition:** the consumer outbox already applies to every endpoint (PLAN-40's `AddConfigureEndpointsCallback`).
5. **Compliance migration (P-3).** The column defaults (`ApprovalStatus=2`, `ApprovedBeforeEngine=true`) are **not** reset to Draft/false, for these reasons:
   - this code always writes both columns;
   - the previous tag, after a rollback, omits them and publishes at once, so its rows must count as published before the engine.

   `OutcomePublishedAt = CreatedAt` is set by an UPDATE inside a DO block that lifts `FORCE` only when it was set, and restores it.
6. **Card:**
   - `CardRequest.ReplacesCardId` is added: the old card is revoked when the replacement is issued.
   - `IssuedCards/Edit` can no longer move a card to another employee, or push its expiry later.
   - The title carries the purpose only, because IDCard has no lookup client; the subject goes in `SubjectEmployeeId`.
   - `AutoSubmitOnHire=true` is refused at start-up: nobody vouches for a request the bus made.
7. **AC-WF-46j:**
   - The key is `PolicyEngine:IDCard.Lifecycle`; the plan's `PolicyEngine:IdCard` is not a domain.
   - It runs in the existing overlay stage (`s2s42`) with `docker-compose.acceptance-wf46.yml`, and `idcard-web` added to `recreate`. A second overlay stage would force a full stack restart in between.
   - It sits in its own class, `AcWf46PolicyOrphanTests`; 46a..i are in `AcWf46CrossServiceApprovalTests` (isolated).
8. **Guard:** `SeparationRecord` is a pending entry (`PendingPlan = PLAN-60`). Its type may publish no definition, and the test fails the day `SeparationApprovalOutcomeConsumer` exists, until the entry is completed.
9. **Tests changed because the behaviour changed on purpose:**
   - `Hcm025ApprovalGatesTests` (an exempt-type test, now "every type is enforced"). It is neither AC-HCM-25 nor `Hcm025PanelServerParityTests`, which are unchanged and green.
   - `Wf043SodRulesTests`, `WorkflowStageGuardRoleTests`, `Wf043DefinitionValidatorTests`, `Wf043DefinitionLifecycleTests`, `Wf043GovernanceIntegrationTests`, `Wf045WorkflowApprovalsControllerTests`, `Phase4Tests`, `Phase5Tests`, `Ops038DemoDefectTests`.
   - Server checks `AcWf43VpsChecks` (12 published, 9 retired, no invalid definition) and `AcOps38VpsDemoDefectChecks`.
   - Acceptance `AcInt37ProductionWiringTests` 37c: the card leg issues from a request seeded approved, since `Renew` issues nothing and a card needs an approved request.
10. **Compliance in the inbox:** a compliance-only user is now in the inbox audience; the layout keeps its appeal menu.

## 4. Inventories

**Server, before and after the deploy:**

| Item | Before (`9560b03e51a3`) | After (`dbf53280121d`) |
|------|-------------------------|------------------------|
| Card requests | 45 Pending, 450 Processed | 47 draft, 453 processed (the checks' requests) |
| Issued cards | 457 | 460 |
| Disciplinary actions | 20 (national tenant) | 22: 20 `Approved` and `ApprovedBeforeEngine`; 2 approved by the engine (the checks) |
| Payroll disciplinary records | 0 | 2, at most 1 per action; the 20 old actions still have none |
| Instances of the six types | 0 | the checks' card and discipline instances |
| Definitions | 16 Published, 3 Retired | 12 Published, 9 Retired |

Definition versions after the deploy:
- `ID-CARD-ISSUANCE` v1 Retired, v2 Published (`idcard`);
- `DISCIPLINARY-APPROVAL` v1 Retired, v2 Published (`compliance`, then `hr-management-approver`, distinct approvers);
- `HIRE-WIZARD`, `TRANSFER-APPROVAL`, `IT-TICKET` and `SEPARATION-WIZARD` unpublished.

**Row security:**
- `CardRequests` and `IssuedCards` (owner `idcard_migrator`) and `DisciplinaryActions` (owner `compliance_migrator`) are enabled and forced, before and after.
- `AuditLogs` has no row security in either database: it is the tenant source of both sweeps.
- `IX_AuditLogs_EntityType_TenantId` was added in both databases.

**Publishers and consumers:**
- `ICardIssued`: only `IssuedCardsController.Create`, from an approved request.
- `IDisciplinaryActionTaken`: only `DisciplinaryApprovalOutcomeConsumer.StageActionTakenAsync`, reached from the consumer and the sweep.
- `IWorkflowOutcomeDecided`: `ExternalOutcomePublisher`.

All three rows are in `docs/EVENT_PUBLISHER_INVENTORY.md`.

**Acceptance data:** the stacks were rebuilt from the seed; see section 6.

## 5. Guards and their own negative tests

| Guard | Its negative test |
|-------|-------------------|
| `WorkflowDefinitionsHaveHandlersTests`: each external type maps to `(ServiceName, ConsumerTypeName)`, found by reflection as an `IConsumer<IWorkflowOutcomeDecided>`; no published definition without a handler or a consumer | `The_guard_finds_nothing_that_is_not_a_consumer` (an existing non-consumer type and a missing name are refused); a pending entry fails once its consumer exists |
| `Wf046RegistryConsistencyTests` | the registry's `Generic` types equal the guard's keys, in both directions |
| `Wf046RegistryEnforcedTests` | no type is exempt in the three settings files or the prod-like compose; no open row in the exemption registry; every seeded code of a registered type passes the validator |
| `Wf046CardIssuanceArchitectureTests` | every `IssuedCards.Add` is in an issuing action that checks `CanProcess` (the demo seeder aside); only the issue from an approved request publishes `ICardIssued`; `Renew` adds no card and publishes nothing |
| `Wf046DisciplinaryPublishTests` | only the consumer file holds `Publish<IDisciplinaryActionTaken>`; the controller publishes nothing |
| `Data001ConcurrencyArchitectureTests` | the rows for `CardRequest` and `DisciplinaryAction` |
| `OutboxPublishOrderingArchitectureTests` | the new publishers are atomic and documented (R1, R4) |
| Start-time guardrail (review fix) | `A_version_that_breaks_the_guardrail_starts_nothing` |

## 6. Results

| Suite | Result |
|-------|--------|
| Build `-warnaserror` | 0 errors |
| Unit, full | 5,531 passed, 0 failed (before the review fixes). After them, the affected filter passed 836/836 and the new tests passed. |
| Integration with Testcontainers, PLAN-46 | 23/23: the outcome event and `Decisions` in the outbox; cancel `Cancelled` and `AlreadyTerminal`; one event on the consumer outbox and none on redelivery; `xmin`; the Compliance migration as a non-superuser owner under `FORCE` in two tenants; both sweeps as the app role in two tenants, paging one record at a time |
| Integration regressions | Wf043, Wf044, Wf045, Ops038, Msg040, CrossTenantRls, Data001, IdCard, Compliance: 105 passed, 1 skipped, 1 expected-list update (fixed) |
| Acceptance prod-like | `AcWf46CrossServiceApprovalTests` 9/9; `AcWf46PolicyOrphanTests` 1/1; AcWf45 8/8; AcWf43 6/6; AcWf44 5/5; AcHcm25ProductionApprovalTests 8/8; AcS2s42 9/9; AcMsg40 8/8; AcOps39 8/8; AcInt37 8/8; `RecruitmentToHireJourneyProdLikeE2ETests` 1/1 |
| Acceptance lab | Ac11 1/1; AcWf23 6/6; AcHcm28 4/4; AcEst24 3/3; Ac33 1/1; `RecruitmentToHireJourneyE2ETests` 1/1; AcceptanceV2CatalogTests 9/9; AcceptanceIdentityPopulationTests 1/1 |
| Validators | `wf043-validate` PASS, `seed-001-validate` PASS, `hcm025-validate` PASS once P4 ended, wiring document up to date |
| Server | 31/31 (section 7) |

The evidence of record is the PR run's acceptance artifact, linked in the merge note. No local evidence is committed; local copies are in `artifacts/plan46/`.

## 7. The server

- **Condition:** the running tag `9560b03e51a3` was on `origin/main`.
- **Backup:** `/opt/hrm/backups/pre-plan46-20261107T224439Z` (700, files 600): `pg_dumpall.sql` with 17 `CREATE DATABASE`, `env.backup`, `SHA256SUMS` verified.
- **Build:**
  - The first attempt failed with "no space left on device": the disk was 95% full.
  - `docker builder prune -af` and `docker image prune -af --filter until=12h` freed 87 GB, leaving the disk at 40%. Every release stays in the loopback registry.
  - The second build took 1,301 s, detached, with 17 of 17 images tagged `dbf53280121d`.
- **Deploy:**
  - The first run stopped at `keycloak-reconcile` ("could not read the realm roles", right after PostgreSQL restarted; the PLAN-40 incident). `hrm-bootstrap` had applied both `WF046_*` migrations as the migrators.
  - The same command again: exit 0 in 116 s, smoke 50 pass, 0 fail, 1 warn.
  - Downtime about 4 minutes. Rollback: `scripts/vps/deploy.ps1 -Rollback` to `9560b03e51a3`.
- **Demo users for the owner's QA guide** (usernames only, all in the national tenant, demo password):

  | Username | Role | Use |
  |----------|------|-----|
  | `e2e-idcard-maker` | `idcard` | requests and processes a card |
  | `e2e-idcard-checker` | `idcard` | approves it from the inbox |
  | `e2e-compliance` | `compliance` | creates a disciplinary action (existing user) |
  | `e2e-compliance-checker` | `compliance` | approves step 1 |
  | `e2e-checker-mgmt` | `hr-management-approver` | approves step 2 (existing user) |

- **Checks:**
  - `AcOps36VpsBootTests` (36j excepted), `AcInt37VpsServerTests` and the PLAN-38..46 server checks: 30/31 on the first run, 31/31 after the sweeps' first cycle.
  - The sweep check had run while `idcard-web` was restarting during the suites.
  - Evidence is under `artifacts/plan46/vps/`.
- **Changes and their undo:** in `docs/OPS_VPS_SINGLE_HOST.md`, section 10 and the PLAN-46 section.

## 8. PLAN-60 alignment

PLAN-60 §0.1 already holds the names, the order and the three corrections (no `EnforceStepRoleEntityTypes`, the owner-side sweep, the `AlreadyTerminal` rule), so its document needed no change. What it reuses:
- the contract, unchanged;
- `WorkflowApprovalSubmission`;
- `WorkflowApprovalSweepJob<TStore>`, through `AddWorkflowApprovalSweep<TStore>`;
- `FakeWorkflowApprovalClient`.

Two things to note for PLAN-60:
- `IWorkflowApprovalOwnerStore.ListPendingAsync` pages on `(SubmittedAt, Id)` and takes an `after` record (a review fix);
- HR.Contracts already registers `AddWorkflowApprovalClient`, and its matrix `calls[]` are in place.

PLAN-60 completes the guard entry `SeparationRecord → SeparationApprovalOutcomeConsumer` and removes `PendingPlan`.

## 9. Deferred items

- **Wave 2, as the plan says:**
  - `ICardIssued` from `ProcessRequest`, and its local serial (G-IDA-03);
  - the card UI actions (G-IDA-04);
  - the discipline model (G-ER-01);
  - AdminLogistics on the shared client.
- **PLAN-49:** removing the policy rules that fire the retired codes. They start nothing (46j).
- **PLAN-61:** dismissal by a disciplinary decision.

## 10. Defects for later plans

1. **An approval of old content (review, medium, not changed):**
   - If the link save fails after a start and the compensating cancel fails too, a later edit and resubmission finds the same key's instance, which is approved on the old facts.
   - It needs two failures in a row, and the approval page links to the owner's current record.
   - Suggested fix: a content hash in the facts, refused on a mismatch.
2. **Overlap with the previous tag (review, low):**
   - On the single host compose stops each old container before the new one, so there is no overlap.
   - A rollback runs the previous tag's rules until the roll-forward: its `Renew` issues at once, and its disciplinary Edit ignores the approval.
3. **Server disk:** the demo server filled to 95% with old release images. The runbook now says to check `df` before a build.
4. **Formatting on Windows:** `dotnet format --verify-no-changes` on Windows reports line endings in files untouched by this plan. CI on Linux is the judge (local CI uses the same container path).

## 11. Timing

| Item | Time |
|------|------|
| Started (UTC) | 2026-11-07T20:33:47Z |
| Inventories (two subagents, in parallel) | 6.5 min |
| Baseline build | 80 s |
| Independent review | 10 min |
| PostgreSQL tests (PLAN-46) | 2 to 3 min a run |
| Acceptance prod-like, run with the image build | 27 min |
| Acceptance prod-like, reruns (`-NoBuild`) | 9 and 8 min |
| Acceptance lab (`-NoBuild`) | 7 min |
| Server backup | 6 s |
| Source upload | 43 s |
| Server build | 22 min (failed, disk full), then 21 min 41 s |
| Deploy | 88 s (stopped) + 116 s |
| Server suites | 16 min, then the sweep check after its first cycle |
| Waiting for CI, Actions minutes, total wall clock | in the progress file's merge note |

`scripts/plan-timing.ps1 -From 25 -To 46`, before the merge:

```
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-43      14 2026-11-06 10:12 2026-11-06 10:21 2026-11-06 19:03  False 8:42   8:50
PLAN-44      14 2026-11-06 21:11 2026-11-06 21:20 2026-11-07 16:25  False 19:05  19:14
PLAN-45      13 2026-11-07 16:55 2026-11-07 16:56 2026-11-07 23:11  False 6:15   6:15
PLAN-46       9 2026-11-07 23:33 2026-11-07 23:34 2026-11-08 03:00  False 3:25   3:26
```

(The script reports local time, UTC+3.)

## 12. Readiness note for PLAN-47 and PLAN-60

**What they reuse:**
- the contract, client and error messages (`WorkflowApprovalErrorCodes.MessageFor`);
- `WorkflowApprovalSubmission` (submit and withdraw);
- `WorkflowOutcomeGuard`;
- `WorkflowApprovalSweepJob<TStore>` with a store over `AuditLogs`, plus an advisory lock key in `AdvisoryLockKeys`;
- `FakeWorkflowApprovalClient`;
- the guard entry in `WorkflowDefinitionsHaveHandlersTests.OutcomeConsumers`;
- `EnsureCrossServiceChainsAsync` as the pattern for raising a chain at start-up.

**Files shared with this plan:** `ExternalApprovalTypes.cs`, `ExternalOutcomePublisher.cs`, `WorkflowPackSeeder.cs`, `WorkflowGuardrails.cs`, `HrManagementAuthorizationExtensions.cs`, `deploy/wiring/service-wiring.json`, `docker-compose.acceptance-prodlike.yml`, `tests/acceptance/ci-scope.json`, `tests/acceptance/ac-ownership.json`, `docs/EVENT_PUBLISHER_INVENTORY.md`.

**For every plan:**
- Check the server disk before a build.
- Server checks need `VPS_TARGET_IP` with the domain variables.
- An overlay class must wait for the stack (`StackReadyAsync`) when it may run first in its stage.
- Agency users see only their tenant's definitions: read national definitions as a national persona.
