# Event Publisher Inventory (INT-001)

Classification of every domain-event publish path after the INT-001 outbox fix.

## Legend

| Class | Meaning |
|-------|---------|
| **Transactional** | EF Core MassTransit outbox enabled; publish is followed by (or precedes) `SaveChangesAsync` so state and event commit together. |
| **Fire-and-forget** | No local aggregate to persist with the event; downstream consumers must be idempotent. |
| **Startup sync** | Bulk re-publish of current state; safe to re-run; not a critical transactional boundary. |

## Services with EF Outbox

| Service | DbContext | Program registration |
|---------|-----------|----------------------|
| HRCore | `HRCoreDbContext` | `AddPostgresOutbox<HRCoreDbContext>()` |
| HRContracts | `ContractsDbContext` | `AddPostgresOutbox<ContractsDbContext>()` |
| Payroll | `PayrollDbContext` | `AddPostgresOutbox<PayrollDbContext>()` |
| HRMovements | `MovementsDbContext` | `AddPostgresOutbox<MovementsDbContext>()` |
| HRHealth | `HealthDbContext` | `AddPostgresOutbox<HealthDbContext>()` |
| IDCard | `IDCardDbContext` | `AddPostgresOutbox<IDCardDbContext>()` |
| Compliance | `ComplianceDbContext` | `AddPostgresOutbox<ComplianceDbContext>()` |
| HRTraining | `TrainingDbContext` | `AddPostgresOutbox<TrainingDbContext>()` |

Delivery is MassTransit `UseBusOutbox()` (polls `OutboxMessage` / `OutboxState`).

---

## Transactional publishers

| Event | Publisher location | Pattern |
|-------|-------------------|---------|
| `IPayrollProcessed` | `PayrollRunHandlers.PostPayrollRunHandler` | Save → Publish → Save |
| `ILeaveApproved` | `LeaveRequestHandlers.ApproveLeaveRequestHandler` | Save → Publish → Save |
| `IAdvanceApproved` | `AdvanceRequestHandlers.ApproveAdvanceRequestHandler` | Save → Publish → Save |
| `IConfirmationApproved` | `ProbationRecordHandlers` | Save → Publish → Save |
| `IJobUpdateApplied` | `AdministrativeDecisionHandlers` | Save → Publish → Save |
| `ISeparationStepCompleted` | `CompleteSeparationChecklistStepHandler` | Save → Publish → Save |
| `IHireCaseCompleted` | `HireCaseHandlers.PublishHireCaseHandler` | ExecuteUpdate → Publish → Save |
| `IContractActivated` | `ActivateContractCommands` | Save → Publish → Save |
| `ICommencementRecorded` | `CommencementRecordHandlers` | Save → Publish → Save |
| `ISeparationCompleted` | `PublishSeparationHandler` | Publish → mutate → Save |
| `IOrgUnitChanged` (create/edit) | `OrgUnitsController` | Save → Publish → Save |
| `IOrgUnitChanged` (delete) | `OrgUnitsController.Delete` | Publish → Save |
| `IEmployeeCreated` / status | `EmployeeIntegrationService` | Save → Publish → Save |
| `ICandidateAccepted` | `RecruitmentIntegrationService` | Publish → Save |
| `ITransactionRegisteredInDiwan` | `DiwanIntegrationService` | Publish → Save |
| `IAttendancePeriodClosed` | `AttendanceIntegrationService` | Publish loop → Save |
| `ITransferExecuted` | `TransferRequestsController` | Save → Publish → Save |
| `ISecondmentExecuted` | `SecondmentRequestsController.Activate` | Save → Publish → Save |
| `ILoanAssignmentExecuted` | `LoanAssignmentRequestsController.Execute` | Save → Publish → Save |
| `IMedicalRecommendationIssued` | `MedicalRecommendationsController` | Save → Publish → Save |
| `IMedicalDecisionIssued` | `HealthExtendedControllers` | Save → Publish → Save |
| `ILegalJudgmentIssued` | `LegalCasesController.Close` | Save → Publish → Save |
| `IDisciplinaryActionTaken` | `DisciplinaryActionsController.Create` | Save → Publish → Save |
| `IComplaintResolved` | `ComplaintsController.Resolve` | Save → Publish → Save |
| `ICardIssued` | `IssuedCardsController.Create` | Save → Publish → Save |
| `ITrainingCompleted` | `TrainingEnrollmentsController` | Save → Publish → Save |
| `IMissionApproved` | `MissionFilesController.Approve` | Save → Publish → Save |
| `ITrainingCompleted` (e-learning) | `EssLearningController` | Publish → Save |

---

## Fire-and-forget

| Event | Location | Rationale |
|-------|----------|-----------|
| `ILeaveRequested` | `EssPortalController` | ESS publishes a request; HRContracts owns persistence. Idempotent consumers required. |
| `IAdvanceRequested` | `EssPortalController` | Same pattern as leave request. |

---

## Startup sync (non-critical)

| Event | Location | Rationale |
|-------|----------|-----------|
| `IOrgUnitChanged` (bulk) | `OrgUnitIntegrationService.PublishAllOrgUnitsAsync` at HRCore startup | Re-sync snapshots; may re-publish. Consumers must tolerate duplicates. |

---

## Required publish pattern

```csharp
// Preferred when IDs already known
await publish.Publish<T>(...);
await db.SaveChangesAsync();

// When ID must exist before event payload
await db.SaveChangesAsync();
await publish.Publish<T>(...);
await db.SaveChangesAsync(); // commits OutboxMessage
```

Never leave `Publish` as the last call without a subsequent `SaveChangesAsync` in an outbox-enabled service.
