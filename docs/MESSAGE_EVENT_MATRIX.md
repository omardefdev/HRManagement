# Message Event Matrix (MassTransit)

**Last updated:** 2026-10-29 — PLAN-32 `IRecruitmentRequisitionClosed` → Dev.Analytics (§2); PLAN-31 competition events → candidate notifications (§5a); PLAN-30 internal application events → candidate notifications (§5a); 2026-05-29 — hire case workflow, checklist, diwan on completion.

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
| `IRecruitmentRequisitionClosed` (v1, HCM-032) | HR.Core — every requisition close: hire closure (last vacancy filled), `RecruitmentRequisitionLifecycleService.CloseAsync` / `CancelAsync`, `EstablishmentChangeApplier` (position freeze); aggregates only | Dev.Analytics `RecruitmentRequisitionClosedConsumer` → `RecruitmentRequisitionFact` | unique (TenantId, RequisitionId), upsert | Yes | `Hcm032AnalyticsPublicationTests` |
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

## 5a. HR.Core internal application events (not bus messages, PLAN-30)

`RecruitmentApplicationEvent` rows (table `RecruitmentApplicationEvents`) are written by
`RecruitmentApplicationEventRecorder` in the **same save** as the change (change tracker only, no PII, append-only except
`NotificationsPlannedAt`). They are **not** published to RabbitMQ; `CandidateNotificationJob` (lock 8675315, every 60 s)
reads rows with `NotificationsPlannedAt IS NULL`, plans at most one `CandidateNotification` per event on the candidate's
chosen channel (key `{eventId}:{channel}`, unique per tenant) and sends it through `INotificationChannelSender`
([COM_001_NOTIFICATIONS.md](./COM_001_NOTIFICATIONS.md)). Only `Public` intake with consent is notified; internal, HR-entered,
legacy, backfilled and suppressed (`SuppressApplicationEvents`) changes send nothing.

| Event (`EventType`) | Trigger | Candidate message (template) |
|---------------------|---------|------------------------------|
| `Received` | `RecruitmentApplication` added (`IntakeChannel = Public` only) | `APP_RECEIVED` |
| `StatusChanged` → `Rejected` / `ReserveListed` / `Hired` / `Withdrawn` | application status changed | `APP_REJECTED` (appeal deadline when appealable) / `APP_RESERVE` / `APP_HIRED` / `APP_WITHDRAWN` |
| `StatusChanged` (other targets) | application status changed | — (marked planned) |
| `InterviewScheduled` / `InterviewRescheduled` / `InterviewCancelled` | interview slot added / added as reschedule / cancelled | `INTERVIEW_SCHEDULED` / `INTERVIEW_RESCHEDULED` / `INTERVIEW_CANCELLED` |
| `OfferIssued` | offer status → `Issued` (still `Issued` at planning) | `OFFER_ISSUED` (no salary) |
| `OfferExpiryReminder` | dispatcher pass: `Issued` offer expiring within `Recruitment:OfferReminderHours` (48), one per offer | `OFFER_REMINDER` |
| `OfferAccepted` | offer status → `Accepted` (candidate on `/careers/my` or HR on behalf) | `OFFER_ACCEPTED` |
| `OfferDeclined` / `OfferExpired` / `OfferRevoked` | offer status changed | — (the application `StatusChanged` covers the outcome) |
| `AppealFiled` | `RecruitmentAppeal` added (HR or the candidate on `/careers/my`) | — |
| `AppealDecided` | appeal `Pending` → decided | `APPEAL_DECIDED` (outcome label only); a result objection (`Kind = ResultObjection`, PLAN-31) → `OBJECTION_DECIDED` |
| `AdmissionCardReady` (PLAN-31) | `ExamSeatingService.IssueAdmissionCardsAsync`, one per seated paper | `ADMISSION_CARD_READY` |
| `ResultsPublished` (PLAN-31) | `CompetitionResultsService.PublishAsync`, one per application of the published version | `RESULTS_PUBLISHED` (no score, no rank) |
| `CompetitionAllocated` / `CompetitionReserve` / `CompetitionNotSuccessful` (PLAN-31) | `CompetitionAllocationService.FinalizeAsync` | `COMPETITION_ALLOCATED` / `COMPETITION_RESERVE` (until date) / `COMPETITION_NOT_SUCCESSFUL` |
| `CompetitionReserveRecalled` (PLAN-31) | `CompetitionReserveService` recall onto a vacated requisition | `COMPETITION_RESERVE_RECALLED` |

PLAN-31: for a competition application the generic `StatusChanged` → `Rejected` (kind `CompetitionResult`) and
→ `ReserveListed` messages are suppressed (the finalize events above carry them); other rejections of a competition
application (cancellation, reserve expiry) keep `APP_REJECTED`. The event type `ObjectionDecided` is mapped to
`OBJECTION_DECIDED` but not recorded today (the `AppealDecided` path carries objections). No PLAN-31 bus contract was
added: an allocated candidate's hire publishes `ICandidateAccepted` v4 unchanged (`RequisitionId` = the allocated requisition).

Not an event: the `/careers/my` one-time code (`EventType = OtpRequested` on its `CandidateNotification` row) is sent
synchronously by `CandidateOtpService` and never queued. The same events drive the candidate timeline
(`CandidatePublicStatusProjection`).

## 6. Messaging infrastructure

| Concern | Configuration |
|---------|---------------|
| Retry | `MassTransit:RetryCount` / `RetryIntervalSeconds` (default 3 × 5s) |
| Correlation | `X-Correlation-ID` on HTTP publish via `CorrelationIdFlow` |
| Outbox | HR.Contracts, HR.Core, Payroll DbContexts |
| DLQ | RabbitMQ `*_error` queues — [RABBITMQ_OPERATIONS.md](./RABBITMQ_OPERATIONS.md) |
| Versioning | Bump `SchemaVersion`; consumers skip unsupported via `EventSchemaVersion` |
