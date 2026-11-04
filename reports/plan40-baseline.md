# PLAN-40 baseline (phase 0)

Branch `feature/plan-40-msg-atomic-event-publishing` from `main` 7e23094f (PLAN-39 merged), 2026-11-02.

## 0.2 Build and the listed filters

| Step | Result |
|---|---|
| `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors, 61 s |
| UnitTests, filter `OutboxAtomicity\|InboxIdempotency\|Qa003Outbox\|RabbitDownOutbox\|Int001\|Int002` | no test matches (all such tests live in IntegrationTests) |
| IntegrationTests, same filter, without `RUN_TESTCONTAINERS` | 14 passed, 5 skipped (Testcontainers) |
| IntegrationTests, same filter, `RUN_TESTCONTAINERS=true` | 18 passed, **1 failed**: `RabbitDownOutboxTests.RabbitDown_OutboxDrains_Once_After_Broker_Restart` (Category=Chaos), "Outbox did not drain after Rabbit restart" |

The Chaos failure is pre-existing and comes from the test, not the outbox: it stops and restarts the Testcontainers RabbitMQ,
which gets a new random host port, while the bus under test keeps the old port. It is fixed in this plan (the plan's
Verification runs `Category=Chaos`).

## 0.3 Publisher inventory

[plan40-publisher-inventory.md](plan40-publisher-inventory.md): 10 class A, 39 class B, 56 class C, 2 web publishes in
services without an outbox, 31 OK, 4 dead wrappers. Every A/B/C site is fixed in P1–P3; the static guard (P4) re-checks the
list.

## 0.4 Live confirmation of class A (prod-like stack, before the fix)

Fresh prod-like stack (`docker compose … -f docker-compose.acceptance-prodlike.yml --profile full-stack up -d --wait`):
`up` 18 s, all 14 applications ready 40 s after `up` (images of PLAN-39). The AC-MSG-40a scenario was run against it
(an approved promotion inserted with SQL, then `POST /ui/hr-movements/PromotionRequests/Execute/{id}` through the gateway as
`a-movements`):

| Check | Value |
|---|---|
| HTTP | 302 (redirect to Details, "تم تنفيذ الترفيع ونشر الحدث") |
| Promotion status | 2 (Executed) — the state committed |
| `OutboxMessage` rows written in `hr_movements_db` (sequence before → after) | **0 → 0** |
| HR.Core receipts for the promotion-executed consumer (before → after 60 s) | **0 → 0** |
| Note in the employee's employment record | **0** |

As expected: the UI says the event was published, the state is committed, and the event never existed. The scenario failed
on its first assertion ("No OutboxMessage row was written"), which is the defect this plan fixes.

## 0.5 Comparison with docs/EVENT_PUBLISHER_INVENTORY.md

Event types published today but absent from the document:

- HR.Core: `IAgencyOnboarded`, `IPolicyPublished`, `IExportJobRequested`, `IAssetAssignmentApproved`,
  `IAssetAssignmentRejected`, `IAssetLossApproved`, `IAssetLossRejected`, `IInternalPlacementCompleted`,
  `IRecruitmentRequisitionClosed`, the hire-closure `IEmployeeCreated`, the onboarding `IOrgUnitChanged`.
- HR.Movements: `IPromotionExecuted`, `IDelegationExecuted`, `ILoanAssignmentEnded`, `IStartTransferSaga`,
  `ICancelTransferSaga`, `IResumeTransferSaga`, `IStartSecondmentSaga`, `ICancelSecondmentSaga`, `IResumeSecondmentSaga`,
  `IExecuteTransferParty`, `IExecuteSecondmentParty`, `IProcessInterventionRequested`, the saga timeout messages.
- HR.Contracts: `ILeaveCancelled`, `IStartSeparationSaga`, `IStartHireSaga`, `ICancelSeparationSaga`,
  `IResumeSeparationSaga`, `IResumeHireSaga`, `ICancelHireSaga`, `IExecuteHireParty`, `IExecuteSeparationParty`,
  `ISeparationSagaFinalized`, `IProcessInterventionRequested`, `IExportJobRequested`, the `ICardIssued` renewal path (IDCard).
- Records.Archive: `ITransactionArchived` (consumer), `IExportJobRequested`; Dev.Analytics, Payroll, Compliance:
  `IExportJobRequested`.
- Every party acknowledgement: `I{Separation,Hire,Transfer,Secondment}Party{Completed,Faulted}` (the shared base classes,
  14 subclasses, Payroll's four party consumers, HR.Core's and HR.Contracts' own).

Wrong rows: `ITransferExecuted`, `ISecondmentExecuted`, `IHireCaseCompleted`, `ISeparationCompleted` are published by the
sagas, not by the listed controllers/handlers (those publish the start-saga commands); every "Save → Publish → Save" row is
labelled Transactional but is class B; the "Startup sync" row hides that the startup re-sync is never saved.

## The demo server before (read-only)

- Tag `817304f5bd88` (`/opt/hrm/current-tag`), 22 containers.
- Outbox tables (`OutboxMessage`, `OutboxState`, `InboxState`) in the eight outbox databases (hr_core, hr_contracts,
  payroll, hr_movements, hr_health, idcard, compliance, hr_training); none in admin_db, auth_directory_db, it_infra_db,
  archive_db, analytics_db. `IntegrationMessageReceipts` everywhere except hr_training_db (and gateway_audit_db).
- RabbitMQ connections: exactly one per `mq_*` user (14), no other user (the extra "user" line in the raw count was
  the `rabbitmqctl` column header).
- Queue depths: every queue empty except `hr-contracts-web-transaction-archived_error` = 3 (pre-existing faulted
  `ITransactionArchived` messages).
