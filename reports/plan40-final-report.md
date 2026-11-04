# PLAN-40 final report — domain events published atomically across the services

Branch `feature/plan-40-msg-atomic-event-publishing` from `main` 7e23094f. Plan:
[PLAN-40](../docs/acceptance-plans/PLAN-40-msg-atomic-event-publishing.md). Progress file with every stamp and undo:
[plan40-progress.md](plan40-progress.md). Review: `artifacts/plan40/review.md` (not committed).

**Outcome:** every publish is written to the outbox in the transaction that commits its state, in all 13 outbox services;
consumers and sagas publish through the consumer outbox; two guards stop regressions; a dry-run replay tool exists; the demo
server runs it. Two things went wrong on the way that the owner should read: an internet exposure of `/metrics` that lasted
about 3 minutes (section 7), and a regression of mine (`IEmployeeCreated` with an empty tenant) that was live on the demo server
for about 75 minutes (section 3). Both are fixed and covered by tests. One review finding stays open (M4, section 8).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | PLAN-39 PR #34 merged after green checks; plain acceptance stack owns its volumes; baseline, inventory, live class-A confirmation (promotion: 302, Executed, 0 outbox rows, 0 receipts, 0 notes) |
| P1 | `OutboxUnitOfWork`, `[StagesOutboxMessages]`, `DeterministicMessageId`; all class A sites fixed |
| P2 | All class B sites become one save or one transaction; inventory document rewritten |
| P3 | Ordering test first (P-1, 8/8), then consumer outbox on 13 services, 5 new outboxes, Training receipts, `PartyAckFlow` |
| P4 | Static guard R1-R4 (Roslyn, test-only), runtime guard in 13 services; allow-list empty |
| P5 | `MessagingRepairController` in HR.Movements and HR.Core, `scripts/msg040-replay-lost-events.ps1`, ledger, audit |
| P6 | AC-MSG-40a..h 8/8, AC-16/17/18 3/3, AcHcm29 3/3, prod-like journey 1/1, every queue empty |
| P7 | Demo server on `f44e52cf58be`; all server checks green; replay dry run recorded, nothing applied |
| P8 | Docs, independent review, this report, merge through a PR |

## 2. Commits

| Commit | Subject |
|---|---|
| 9cd28dd3 | docs: PLAN-40 agent prompt |
| 044be937 | docs(ops039): PR merge note and post-merge run |
| 22f9d2f1 | fix: plain acceptance stack owns its volumes; evidence summary keeps its log, UTF-8 without BOM |
| 491c7b5d | chore: baseline, inventory, live class-A confirmation, server before-picture |
| 46b019b4 | fix: class A |
| 78742634 | fix: class B, inventory corrected |
| caea63c1 | feat: consumer outbox, five outboxes, Training receipts, party acknowledgements once |
| 83aab24c | feat: static and runtime guards |
| 4de387c0 | feat: dry-run-first replay |
| f0490b3e | fix: asset entity types kept in the outcome dispatch |
| c8872d77 | test: acceptance green; defects found live fixed |
| 63f041ab | fix: review findings |
| 5c0f8f1b | fix: gateway requests told by proof and forwarded headers |
| f44e52cf | test: acceptance evidence on the final code |
| 381a5349 | test: demo server checks |

## 3. Decisions applied and deviations

D-40-1..6 applied. Deviations from the plan or the prompt, each with its reason:

- **Inbox receipt inside the outbox (plan §4).** MassTransit's consumer outbox runs two transactions per message; the first only
  inserts `InboxState`. A receipt staged before it, as the plan said, would commit before the consumer ran, and a failed consumer
  would be skipped as a duplicate on retry. `InboxReceiptConsumeFilter` sits inside the outbox on the consumer and saga message
  pipes. `InboxConsumeFilter` passes through; its transaction stays for File.Storage.
- **`ApproveAsync`/`RejectAsync` not wrapped (prompt P1).** Asset outcomes are staged before the single save (D-40-2's default).
  Handlers that run after completion open transactions unconditionally and some roll back and return a result, so joining them
  would change their semantics. Open risk: M4, section 8.
- **Replay ledger (plan §6 rule 1).** Consumer receipts are in other databases. `PublishedEventLedger` writes an append-only
  `EntityAuditLog` row in the same save as each replayable publish; lost = committed source without a row.
- **D-40-6 detail.** `PartyAckFlow` rolls back to a savepoint before publishing the fault: without it partial work would commit,
  and a failed SQL statement would abort the outbox transaction.
- **Authorization.** One policy `Messaging.Repair` (`hr-admin` or `platform-admin`). The gateway's module policies admit `hr-admin`
  only, so the script runs with a tenant's `hr-admin`, once per tenant.
- **P-3.** `ConfigureMassTransitOutbox` also calls `AddInboxStateEntity`; seven existing contexts get `MSG040_InboxStateKeys` (one
  index). `ServiceTransactionStateHistory.Id` is no longer generated.
- **Step 0.3.** The inventory came from two read-only passes, not a throwaway Roslyn scan; the guard of P4 re-checked it and found
  no unknown A or B site.
- **P-7/P-8 "build once".** Three server builds, because the review found H1 on the deployed tag and the first M2 fix was too wide.
- **Regression of mine, H1.** In P2 I removed the first save of `CreateEmployeeWithEventAsync`, but the tenant in the payload is
  stamped by that save. `IEmployeeCreated` left with an empty `TenantId` for employees created in the HR UI on tag `c8872d7767ab`
  (01:51Z-03:08Z on 2026-11-04). The tests I wrote checked ordering, not the payload, and no test called that method. Verified on
  the server: 0 employees were created in that window, so no data was affected. Fixed in
  63f041ab: save first inside `OutboxUnitOfWork`, with a test using the real interceptor.

## 4. The publisher inventory, before and after

| Class | Before | After |
|---|---|---|
| A: publish with no later save | 10 | 0 |
| B: save, publish, save without a transaction | 39 | 0 |
| C: publish from a consumer or saga without a consumer outbox | 56 | 0 (all through the consumer outbox) |
| Web publish in a service without an outbox | 2 | 0 |
| OK | 31 | all |

Site by site: [plan40-publisher-inventory.md](plan40-publisher-inventory.md). Current patterns:
[EVENT_PUBLISHER_INVENTORY.md](../docs/EVENT_PUBLISHER_INVENTORY.md). `outbox-publish-allowlist.txt` is empty, limit 0.

## 5. The guards and their negative tests

- **Static**, `OutboxPublishOrderingArchitectureTests` (18 tests): R1 no save after a publish; R2 a write before a publish outside a
  transaction (beyond the plan, `ExecuteUpdate`/`ExecuteDelete` count as writes); R3 `context.Publish` without `consumerOutbox: true`;
  R4 a type missing from the inventory. Each rule has a broken sample that must fail, and the real run must see over 100 publish
  sites. On first run against the real tree it flagged 14 sites, which led to 4 more `[StagesOutboxMessages]` and one
  `ExecuteAtomicAsync`, and showed false positives that the branch rule now removes. Limits: order by position, saves by name, and
  `var` send endpoints are not seen (review L1).
- **Runtime**, `UnsavedOutboxGuardMiddleware` in 13 services: `Msg040UnsavedOutboxGuardIntegrationTests` 5/5 (throws in Testing,
  Acceptance and with the switch in Production; counts and passes in Production; publish then save passes).

## 6. Results

| Suite | Result |
|---|---|
| Unit | 4623/4623 on 63f041ab, plus the IsProxied theory change; local CI numbers in the progress file |
| Integration (Testcontainers on) | 474+ passed; only the 3 `Qa003Minio` tests fail (CI-excluded `ObjectStorage`, unpullable image) |
| `Msg040*` integration | ordering 8, unit of work 4, guard 5, replay 1, movements 2, history 1, org-unit rollback 1 |
| `RabbitDownOutboxTests` (Chaos) | failed at baseline, passes now (own broker on a fixed port, delivery service started) |
| AC-MSG-40a..h | 8/8, 100 s |
| AC-16, AC-17, AC-18 live | 3/3, 66 s; replaces the failing AC-16 evidence. AC-17 reworked for the accepted-offer hire |
| AcHcm29 | 3/3, 35 s |
| Prod-like journey | 1/1, 17 s |
| 13 x `has-pending-model-changes` | none |
| Ops038 least-privilege, permission matrix, env parity, lint-compose both chains, wiring matrix, realm parity | green; no broker name added, `rabbitmq-users.json` unchanged |

**Defects the live run found, none visible to unit tests:** `IPolicyPublished` dropped by the broker (a concrete publish through the
outbox reaches an exchange nothing binds); transient database failures sent straight to `_error` (EF wraps them in
`InvalidOperationException`); `/metrics` answering 401; connections exhausted on the start-up re-sync; state-history rows from
consumers failing on every delivery (the cause of the 3 old server `_error` messages); the start-up re-sync covering the default
tenant only.

## 7. The server

**Before:** tag `817304f5bd88`, 22 containers, outbox tables in 8 databases, 14 `mq_*` connections, queues empty except
`hr-contracts-web-transaction-archived_error` = 3.

**After:** tag `f44e52cf58be`, 22 containers, smoke 50 pass / 0 fail / 1 warn (the known VPN port probe). Outbox tables in the five,
Training receipts, 14 `mq_*` connections only, `outbox_unsaved_messages_total` 0 in 13 services after the tour, `_error` unchanged.

**Replay dry run (not applied, D-40-4):** 0 promotions, 0 delegations, 0 asset outcomes; 30 org units for the re-sync.
[msg040-replay-server-20261104T031223Z.json](msg040-replay-server-20261104T031223Z.json). The zeros are real: the demo has no
promotion, delegation or asset-workflow rows. The owner's command is in
[OPS_VPS_SINGLE_HOST §14](../docs/OPS_VPS_SINGLE_HOST.md).

**Incidents:**

1. **`/metrics` reachable from the internet, 01:51:48Z-01:54:37Z.** I set `HRM_OTEL_ENABLED=true` for the P7 check while `/metrics`
   had just become anonymous, and `https://<app>/careers/metrics` answered 200 (the careers route is anonymous at the gateway and
   HR.Core strips its path base, so the address check saw the gateway's private IP). Content: aggregate runtime metrics, no personal
   data. The independent review found it; I confirmed it live and closed it by reverting the key and redeploying. Now refused in the
   filter and at Caddy, and checked by `smoke.sh`, AC-36c and AC-40f. The key is on again and `/careers/metrics` answers 404.
   `docker-compose.production.yml` defaults telemetry on, so this would have reached production.
2. **Deploy failure, demo down 3 min 2 s (03:05:23Z-03:08:25Z).** PostgreSQL was recreated, Keycloak's pooled connections closed,
   `keycloak-reconcile` failed, the applications stayed `Created`. The same deploy command brought it back. No data touched.

**Server changes and undo:**

| Change | Undo |
|---|---|
| Release `f44e52cf58be`, `MSG040_*` migrations | `deploy.ps1 -Tag 817304f5bd88` (the old code ignores the new tables). Do not use `-Rollback`: previous-tag is `c8872d7767ab`, which has H1 |
| `HRM_OTEL_ENABLED=true` | set `false`, then deploy the current tag |
| `/opt/hrm/backups/pre-plan40-20261104T014945Z` (700/600, verified, 17 `CREATE DATABASE` lines, `SHA256SUMS`) | remove when no longer needed |
| `/opt/hrm/shared/.env.bak-plan40-20261104T014945Z` | remove when no longer needed |

The backup was taken before the first deploy; the later deploys added no migration.

## 8. Deferred items

- **M4 (review, open):** handlers for non-asset workflows (requisition, offer, committee, appeal, establishment change, competitions)
  still run after the instance commits. If one throws, the instance is `Completed`, the effect and its events never happen, and no
  replay exists. Needs the five transaction-opening services to join an outer transaction, or a durable "outcome pending" row.
- Out of scope as the plan says: promotion semantics (Wave 2), PLAN-45, PLAN-46, PLAN-47 §8, PLAN-62/63, the full retry policy,
  `_error` tooling and alerting, cross-service reconciliation, RLS, Training ordering.
- Applying the replay on the server: the owner's decision; nothing to apply today.
- The 3 old `transaction-archived_error` messages on the server stay in the queue: the consumer now works, so re-queuing them is safe,
  but `_error` tooling is Wave 2.

## 9. Defects for later plans

- `keycloak-reconcile` should retry a read after PostgreSQL restarts (incident 2).
- `ConcurrentMessageLimit` is per endpoint: a burst across many endpoints can still reach the pool (review L3).
- Static guard blind spots (L1): `var` send endpoints, names `context`/`ctx` exempt from R1/R2, unit-of-work bodies matched by name per
  file.
- `ProcessPartyAckConsumers` compensation and the Contracts compensate branch are outside `PartyAckFlow`: a failure there reaches
  `_error` with no fault (L7). With a dead connection on the last attempt no fault can be written (L5).
- `OutboxScope.EnsureFlushedAsync` is used once, and redundantly; hosted jobs rely on R1 alone (L8).
- `DataImportController.ImportEmployees` never adds the employee; `ImportOrgUnits` publishes nothing.
- Dead wrappers: `PublishTransactionArchivedAsync`, `PublishSeparationCompletedAsync`, `PublishHireCaseCompletedAsync`.
- `docker/build-manifest` must be regenerated after any lock file change (NU1004 otherwise).
- AC-40e proves the party outbox with a saga command; the hire journey covers the parties it reaches, and Admin.Logistics is not one.

## 10. Timing

- **Wall-clock:** started 2026-11-02T08:01:20Z; report written 2026-11-04T03:30Z (43 h 29 min).
- **Active vs wait:** no owner action was needed. The session was interrupted on 2026-11-02 during P1 and resumed on the owner's «اكمل»
  at 2026-11-03T23:12:20Z. Active time is about 4 h 40 min (P0 21 min, P1 before the stop a few minutes more, then 4 h 15 min).
- **Phases (UTC):** P0 08:01-08:23; resume 23:12; P1 -23:18; P2 -23:41; P3 -00:02; P4 -00:15; P5 -00:23; P6 -01:28; P7 -03:27 (three server
  builds and the review fixes); P8 from 03:27.
- **Inventory:** two subagents in parallel, 6.3 and 7.5 min.
- **Image builds:** local 15 images 556, 514, 502, 510, 540 s (five rounds: each live defect needed one). Server 1188, 1176, 1167 s.
- **Stack starts:** prod-like `up` 18-21 s, all ready 43-46 s.
- **Acceptance:** AC-MSG-40 97-100 s; AC-16/17/18 62-71 s; AcHcm29 33-35 s; journey 16-17 s.
- **Server:** deploys 131, 113, 75 (failed), 115 s; downtime 3 min 2 s on the failed one; AC-OPS-36 + 39 suite 710 s; AcInt37 35 s; backup 3 s.
- **Actions minutes, PR checks and the post-merge guard:** in the progress file after the merge. Public repository, billable 0 so far.
- `powershell -File scripts/plan-timing.ps1 -From 25 -To 40` (before the merge; local time, UTC+3):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-38      18 2026-11-01 13:24 2026-11-01 13:25 2026-11-02 02:13  False 12:47  12:48
PLAN-39      13 2026-11-02 02:11 2026-11-02 02:13 2026-11-02 11:05  False 8:52   8:53
PLAN-40      14 2026-11-02 11:01 2026-11-02 11:05 2026-11-04 06:26  False 43:21  43:25
```

(PLAN-25..37 rows unchanged. PLAN-40's span includes the interruption of about 39 hours.)

## 11. Readiness note for PLAN-41

- **P-5 carry-over done:** `docker-compose.acceptance.yml` has `acceptance-volume-init`; the summarizer keeps its log and writes UTF-8
  without a BOM. The acceptance stack now has `max_connections=250` and `OpenTelemetry__Enabled=true`.
- **P-9 contract:** any plan that adds an event or a publisher adds its row to `EVENT_PUBLISHER_INVENTORY.md` in the same commit (R4) and
  publishes with its patterns: publish through the contract interface, then one save; `OutboxUnitOfWork` where a database value is
  needed; the consumer outbox from consumers. A payload field stamped at save time (the tenant) needs the save first, inside the
  unit of work. No allow-list line without a written reason.
- Run the acceptance filters one group at a time: AC-17, 40d and 39f stop or recreate containers and interfere with parallel classes.
- Server: tag `f44e52cf58be`; `previous-tag` is `c8872d7767ab`, do not roll back to it.
