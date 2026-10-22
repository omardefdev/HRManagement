# Message Event Matrix (MassTransit)

**Last updated:** 2026-05-29 — hire case workflow, checklist, diwan on completion.

Event categories: **domain** | **workflow integration** | **external seam** | **dead-end**.  
Publishing rules: [EVENT_MESH_AUDIT.md](./EVENT_MESH_AUDIT.md) | Idempotency: [INBOX_IDEMPOTENCY.md](./INBOX_IDEMPOTENCY.md) / [IDEMPOTENCY_STRATEGY.md](./IDEMPOTENCY_STRATEGY.md) | Sagas: [PROCESS_SAGAS.md](./PROCESS_SAGAS.md)

---

## 1. Domain events (wired)

| Event | Publisher | Consumers | Idempotency | Tests |
|-------|-----------|-----------|-------------|-------|
| `IEmployeeCreated` | HR.Core `EmployeeIntegrationService` | HR.Contracts, HR.Health, IDCard, Auth.Directory, Payroll, IT.Infra, Dev.Analytics | Inbox filter (+ markers) | Payroll, IT, Analytics, Health |

> **GOV-003:** `IEmployeeCreated` / `IEmployeeStatusChanged` are SoR publish channels for master-employee.v1 — consumers must not rewrite NationalId/FullName.
| `IEmployeeStatusChanged` | HR.Core | Auth.Directory | Inbox + guard | — |
| `IContractActivated` | HR.Contracts | Payroll | Inbox + marker | Unit / Idempotent, `Fun029ContractActivatedTests`, `Pay029PayProfileTests` |
| `ISeparationCompleted` | HR.Contracts | Payroll, Archive, IDCard, Admin.Logistics, Auth.Directory, IT.Infra, Dev.Analytics | Inbox (+ ref/marker) | Payroll, Analytics, Admin |
| `IMedicalRecommendationIssued` | HR.Health | Compliance | Inbox + marker | Unit |
| `IMedicalDecisionIssued` | HR.Health | HR.Contracts | Inbox | — |
| `ILegalJudgmentIssued` | Compliance | HR.Contracts | Inbox | — |
| `IDisciplinaryActionTaken` | Compliance | Payroll | `ActionId` marker | Payroll, Unit |
| `IPayrollProcessed` | Payroll | Dev.Analytics | payload `referenceKey` | — |
| `ICardIssued` | IDCard | HR.Contracts `CardIssuedHireChecklistConsumer` | checklist step | — |
| `IComplaintResolved` | Compliance | Dev.Analytics, Records.Archive | payload `referenceKey` / ref `CMP-RES-*` | — |
| `ICardIssued` | IDCard | HR.Contracts, Dev.Analytics | checklist / payload ref | Integration |
| `ISeparationStepCompleted` | HR.Contracts | — (workflow telemetry) | — | — |
| `ILoanAssignmentExecuted` | HR.Movements | — (v1 publish) | — | — |

## 2. Workflow integration events

| Event | Publisher | Consumers | Idempotency | Outbox | Tests |
|-------|-----------|-----------|-------------|--------|-------|
| `ILeaveApproved` / `IAdvanceApproved` | HR.Contracts | Payroll, HR.Core, Archive | marker / ref / reason | Yes | Payroll, Unit |
| `ITransferExecuted` | HR.Movements | Payroll, Auth.Directory, HR.Core, Admin.Logistics | Payroll: marker; Admin: inbox | — | Admin |
| `ISecondmentExecuted` | HR.Movements | Payroll, Auth.Directory | Payroll: marker; Auth: audit | — | Payroll |
| `ITrainingCompleted` | HR.Training | HR.Core, Payroll | `IntegrationMessageReceipts` / Payroll marker | — | HR.Core, Payroll |
| `IMissionApproved` | HR.Training | Payroll | Payroll: marker | — | Payroll |
| `ICandidateAccepted` (v4) | HR.Core `RecruitmentHireClosureService` — external hires only (HCM-029: internal mobility publishes `IInternalPlacementCompleted` only) | HR.Contracts | Contract exists check | Yes | Inbound, `Hcm029CandidateAcceptedV4Tests` |
| `IInternalPlacementCompleted` | HR.Core `RecruitmentHireClosureService` (internal mobility) | HR.Movements `InternalPlacementCompletedConsumer` | consumer | Yes | `InternalHireClosureIntegrationTests` |
| `IProcessInterventionRequested` (Hire, outside the saga) | HR.Contracts — unresolved contract type (`ProcessCandidateAcceptedHandler`), overdue commencement (`HireCommencementWatcher`) | HR.Contracts `ProcessInterventionRequestedConsumer` | one open intervention per (Hire, transaction) | Yes | `Fun029ContractTypeResolverTests`, `Fun029CommencementWatchTests` |
| `ITransactionRegisteredInDiwan` | HR.Core `DiwanIntegrationService` | HR.Contracts | Transaction idempotent update | Yes | Inbound |
| `IHireCaseCompleted` | HR.Contracts `PublishHireCaseHandler` | Records.Archive, HR.Core (ديوان) | Archive ref `HIRE-{txn}`; Diwan ref | Yes | Integration |

## 3. External integration seams (consumed, no in-repo publisher)

| Event | Consumer | Intended owner | Test publisher |
|-------|----------|----------------|----------------|
| `ILeaveRequested` | HR.Contracts | Employee portal | Integration tests only |
| `IAdvanceRequested` | HR.Contracts | Finance portal | Integration tests only |
| `IJobChangeRequested` | HR.Contracts | HR.Movements (future) | Integration tests only |
| `IResignationSubmitted` | HR.Contracts | Employee portal | Integration tests only |
| `IEmployeeOfferAccepted` | HR.Contracts | Recruitment portal | Integration tests only |
| `IProbationEvaluationCompleted` | HR.Contracts | HR evaluation | Integration tests only |

## 4. Dead-end events (published, no v1 consumer)

`ICommencementRecorded`, `IConfirmationApproved`, `IJobUpdateApplied`, `ITransactionArchived` — HR.Contracts handlers; deferred consumers.

## 5. Workflow fixes (v1)

| Behavior | Detail |
|----------|--------|
| Duplicate contract on hire | `ICandidateAccepted` ينشئ العقد/المعاملة؛ `EmployeeCreatedConsumer` يتخطى العقد عند وجود `Appointment` ويُعلّم P2.1 فقط |
| Hire checklist | `HireChecklists` + `HireWorkflowRules` + `HireWizardController` |
| Diwan on hire complete | `HireCaseCompletedDiwanConsumer` → `ITransactionRegisteredInDiwan` → أرشفة المعاملة |
| Leave/advance | Single `ServiceTransactionId` per request; approval completes same transaction |
| Separation | `PublishSeparationHandler` + `SeparationEventPublished` flag |
| Leave → HR.Core status | `LeaveApprovedConsumer` sets `OnLeave` with reason marker |
| Offer terms to payroll (PLAN-29) | `ICandidateAccepted` v4 terms → `Contract.GradeCode/AgreedBaseSalary/LegalRegimeId/EmploymentType`; `IContractActivated.BaseSalary = AgreedBaseSalary`, `PayGradeCode = GradeCode` (null for contracts created outside recruitment; members exist since v2 — no bump); Payroll resolves the grade in the scale in force at `StartDate`, out-of-range/no salary → review hold |

## 6. Messaging infrastructure

| Concern | Configuration |
|---------|---------------|
| Retry | `MassTransit:RetryCount` / `RetryIntervalSeconds` (default 3 × 5s) |
| Correlation | `X-Correlation-ID` on HTTP publish via `CorrelationIdFlow` |
| Outbox | HR.Contracts, HR.Core, Payroll DbContexts |
| DLQ | RabbitMQ `*_error` queues — [RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md) |
| Versioning | Bump `SchemaVersion`; consumers skip unsupported via `EventSchemaVersion` |
