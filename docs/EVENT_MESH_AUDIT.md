# Event Mesh Audit (MassTransit + RabbitMQ)

**Audit date:** 2026-05-28  
**Contracts:** 30 interfaces in `HRManagement.MessageContracts/Events`  
**Transport:** MassTransit 8.x over RabbitMQ via `HRManagement.ServiceDefaults/MassTransitExtensions.cs`

## Architecture categories

| Category | Definition |
|----------|------------|
| **Domain events** | State change inside one bounded context |
| **Workflow integration events** | Cross-service handoff with in-repo publisher and consumer |
| **External integration seams** | Consumer exists; ingress from outside the repo (portals, future modules) |
| **Dead-end events** | Published intentionally with no v1 consumer |

See also [MESSAGE_EVENT_MATRIX.md](./MESSAGE_EVENT_MATRIX.md), [IDEMPOTENCY_STRATEGY.md](./IDEMPOTENCY_STRATEGY.md), [RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md).

## Publishing reliability

| Rule | Status |
|------|--------|
| Publish after persist | Enforced via MassTransit EF outbox on HR.Contracts, HR.Core, Payroll |
| No controller fire-and-forget | HR.Core employee/recruitment/Diwan flows use application services + outbox |
| Consumer idempotency | Two-tier: `IntegrationMessageReceipts` inbox + business-key markers |

## Correctly wired (15 events)

| Event | Publisher | Consumers |
|-------|-----------|-----------|
| `IEmployeeCreated` | HR.Core | HR.Contracts, HR.Health, IDCard, Auth.Directory, Payroll, IT.Infra, Dev.Analytics |
| `IEmployeeStatusChanged` | HR.Core | Auth.Directory |
| `ICandidateAccepted` | HR.Core | HR.Contracts |
| `IContractActivated` | HR.Contracts | Payroll |
| `ISeparationCompleted` | HR.Contracts | Payroll, Archive, IDCard, Admin.Logistics, Auth.Directory, IT.Infra, Dev.Analytics |
| `ILeaveApproved` / `IAdvanceApproved` | HR.Contracts | Payroll, HR.Core, Archive |
| `ITransferExecuted` | HR.Movements | Payroll, Auth.Directory, HR.Core, Admin.Logistics |
| `ISecondmentExecuted` | HR.Movements | Payroll, Auth.Directory |
| `ITrainingCompleted` | HR.Training | HR.Core, Payroll (training premiums when enabled) |
| `IMissionApproved` | HR.Training | Payroll |
| `IMedicalRecommendationIssued` | HR.Health | Compliance |
| `IMedicalDecisionIssued` | HR.Health | HR.Contracts |
| `ILegalJudgmentIssued` | Compliance | HR.Contracts |
| `IDisciplinaryActionTaken` | Compliance | Payroll |
| `IPayrollProcessed` | Payroll | Dev.Analytics |

## Workflow integration — newly wired

| Event | Publisher | Consumer | Reliability |
|-------|-----------|----------|-------------|
| `ITransactionRegisteredInDiwan` | HR.Core Diwan service | HR.Contracts | Transactional outbox |

## Dead-end events (7)

| Event | Publisher | Notes |
|-------|-----------|-------|
| `ICommencementRecorded` | HR.Contracts | v1 deferred consumer |
| `IConfirmationApproved` | HR.Contracts | v1 deferred consumer |
| `IJobUpdateApplied` | HR.Contracts | v1 deferred consumer |
| `ITransactionArchived` | HR.Contracts | v1 deferred consumer |
| `ICardIssued` | IDCard | By design |
| `IComplaintResolved` | Compliance | By design |
| `ISeparationStepCompleted` | — | Removed unused publisher method |

## External integration seams (6)

No in-repo production publisher. Integration tests may publish via test harness only.

| Event | Consumer | Intended external owner |
|-------|----------|---------------------------|
| `ILeaveRequested` | HR.Contracts | Employee self-service portal |
| `IAdvanceRequested` | HR.Contracts | Finance / employee portal |
| `IJobChangeRequested` | HR.Contracts | HR.Movements (future) |
| `IResignationSubmitted` | HR.Contracts | Employee separation portal |
| `IEmployeeOfferAccepted` | HR.Contracts | Recruitment offer portal |
| `IProbationEvaluationCompleted` | HR.Contracts | HR evaluation module |

HR.Contracts creates leave/advance/separation synchronously via MVC for staff workflows — do not add self-publish anti-pattern.

## Idempotency gaps closed (this rollout)

| Service | Consumer | Mechanism |
|---------|----------|-----------|
| HR.Health | `EmployeeCreatedConsumer` | Inbox + existing record check |
| Compliance | `MedicalRecommendationIssuedConsumer` | Marker on `RecommendationId` |
| Admin.Logistics | `TransferExecutedConsumer`, `SeparationCompletedConsumer` | Inbox |
| IT.Infra | `SeparationCompletedConsumer` | Marker + inbox |
| IDCard | `SeparationCompletedConsumer` | Guard: skip if no active cards |
| Auth.Directory | `SeparationCompletedConsumer`, `EmployeeStatusChangedConsumer` | Guard inactive state |
| Payroll | `DisciplinaryActionTakenConsumer` | Stable `ActionId` marker |
| HR.Contracts inbound | `ProcessJobChangeRequestedHandler`, `ProcessSeparationRequestHandler` | Request-level dedup |

## Event versioning

Contracts expose `SchemaVersion` (default interface property). Consumers use `EventSchemaVersion.Supports()` — unsupported versions log warning and skip (no poison loop).

Procedure: bump contract version → deploy consumers → deploy publishers.

## Retry and DLQ

- Retry: `MassTransit:RetryCount` / `RetryIntervalSeconds` (default 3 × 5s) on all services
- DLQ: MassTransit default `{queue}_error` on RabbitMQ after retries exhausted
- Fault observer logs exhausted retries to Serilog
