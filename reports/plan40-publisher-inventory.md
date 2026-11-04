# PLAN-40 publisher inventory (step 0.3)

Branch `feature/plan-40-msg-atomic-event-publishing` at 7e23094f (2026-11-02). Every `Publish`/`Send`/`Schedule` site in
`src/Services/**` and `src/Shared/**`, with each wrapper's callers classified:

- **A** — publish with no `SaveChanges` after it in the scope (the event is lost);
- **B** — save → publish → save without one transaction (state can commit without its event);
- **C** — publish from a consumer or saga (`ConsumeContext`), sent to the broker before the inbox transaction commits;
- **NO-OUTBOX** — a web publish in a service without an outbox (straight to the broker after the save);
- **OK** — change → publish → one save, or inside one explicit transaction.

**Method (deviation from step 0.3):** the inventory was built by two read-only passes (one per service group) that
followed every publish call and every wrapper to its callers, line by line. The Roslyn scan was not run as a separate
throwaway tool: it is written once, as the static guard of P4 (`OutboxPublishOrderingArchitectureTests`), which then
re-checks this list mechanically (any site it finds that is not here is added in P4).

## Totals (before PLAN-40)

| Group | A | B | C | NO-OUTBOX | OK | dead |
|---|---|---|---|---|---|---|
| HR.Core + HR.Movements | 10 | 16 | 17 | 0 | 23 | 0 |
| Other services + Shared | 0 | 23 | 39 | 2 | 8 | 4 |
| **Total** | **10** | **39** | **56** | **2** | **31** | **4** |

Sites needing a database value (so an explicit transaction, `OutboxUnitOfWork`, rather than one save): HR.Movements
`TransferRequestsController.Execute` (`ExecuteUpdate` compare-and-set), HR.Contracts `PublishHireCaseHandler`,
`ApproveLeaveRequestHandler`, `CancelLeaveRequestHandler` (`ExecuteUpdate` claims whose row counts decide the outcome),
and the save-inside helpers of HR.Contracts (`HireCaseSupport`) and HR.Core (`OrgUnitPathService.AssignPathOnCreate`,
`OrgUnitVersionService.AppendVersion`, `MoveSubtreeAsync` with its own transaction). None needs a value produced by a
trigger or a database sequence after the entity exists (no Stop condition).

Findings outside atomicity (recorded, not fixed here unless a fix touches the same lines): `DataImportController.ImportEmployees`
never adds the employee; `DataImportController.ImportOrgUnits` publishes nothing; `PolicyTemplatesController.PublishFromUi`
did not publish `IPolicyPublished` (fixed by moving the publish into `PolicyVersionPublisher`, P1); dead wrappers
`PublishTransactionArchivedAsync`, `PublishSeparationCompletedAsync`, `PublishHireCaseCompletedAsync`.

---

## Group 1: HR.Core and HR.Movements

Scope: `src/Services/HRCore/**` and `src/Services/HRMovements/**`, read on branch `feature/plan-40-msg-atomic-event-publishing`.
Both services register the EF bus outbox: HR.Core `src/Services/HRCore/HR.Core.Web/Program.cs:268-271`, HR.Movements `src/Services/HRMovements/HR.Movements.Web/Program.cs:64`, both via `AddPostgresOutbox` (`src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs:120-129`, with `UseBusOutbox()` and no consumer outbox).
The searches covered `.Publish(`, `.Publish<`, `.Send(`/`.Send<`, `PublishBatch`, `GetSendEndpoint`, `IPublishEndpoint`, `ISendEndpointProvider`, `IBus`, `SchedulePublish`/`ScheduleSend`/`RespondAsync`, saga `Schedule(...)`, every wrapper name and each wrapper's callers, hosted services and `Program.cs`. They found no `Send`, `PublishBatch`, `IBus` or `ISendEndpointProvider` use in scope.
None of the HR.Core `BackgroundService` jobs (`Program.cs:154-176`) publishes, directly or through a wrapper. No `EnableRetryOnFailure` execution strategy exists anywhere in `src/`, so a plain `BeginTransactionAsync` is valid.

Classes: **A** means no save after the publish, so the event is lost. **B** means save, then publish, then save, with no explicit transaction, so a crash between the saves leaves the state without the event. **C** means a consumer or saga publish, which goes straight to the broker. **OK** means a single save or one explicit transaction.

| # | Class | Event | Publish site (file:line) | Flow (save/publish order with file:line of each save, or the caller for wrappers) | DB value needed? | Proposed fix |
|---|---|---|---|---|---|---|
| 1 | A | IAgencyOnboarded | src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs:169 | Caller `OnboardingController.Apply` (src/Services/HRCore/HR.Core.Web/Controllers/OnboardingController.cs:20). `ApplyManifestAsync` saves at AgencyProvisioningService.cs:167 (SeedPackApplication), publishes at :169, then returns. The controller returns `Ok` at :21. Nothing flushes the outbox, so the event is lost. | No | Publish before the save at :167 (move :169-178 above :167). Better, wrap the whole `ApplyManifestAsync` (many saves: :103, NationalSeedOrchestrator, org structure, :109/:270/:341/:167) in one explicit transaction. |
| 2 | A | IAgencyOnboarded | src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs:169 | Caller `OnboardingController.ApplyFromFile` (OnboardingController.cs:32). Same flow as row 1. | No | Same as row 1. |
| 3 | A | IOrgUnitChanged (bulk, every unit) | src/Services/HRCore/HR.Core.Infrastructure/Integration/OrgUnitIntegrationService.cs:13 (via `PublishAllOrgUnitsAsync` :31-36, loop :35) | Startup scope in src/Services/HRCore/HR.Core.Web/Program.cs:299-328. `RecomputeAllAsync` saves (OrgUnitPathService.cs:24) at Program.cs:320. `PublishAllOrgUnitsAsync` at Program.cs:321 then adds N outbox rows, and the scope ends at :328 with no save. Every startup re-sync is silently discarded. | No | Add `SaveChangesAsync` at the end of `PublishAllOrgUnitsAsync`. Or publish inside `RecomputeAllAsync` before its save, so paths and events commit together. |
| 4 | B | IOrgUnitChanged (bulk, tenant units) | OrgUnitIntegrationService.cs:13 via `PublishAllOrgUnitsAsync`, called at src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyOrgStructureProvisioner.cs:76 | Caller AgencyProvisioningService.cs:119 (from OnboardingController :20/:32). Saves before the publish: AgencyOrgStructureProvisioner.cs:70 (per unit), OrgUnitPathService.cs:121 (AssignPathOnCreate), OrgUnitPathService.cs:24 (RecomputeAll at :75). Then the publish at :76. The outbox is flushed by the next save: MdmReferenceSeeder.cs:109 (src/Services/HRCore/HR.Core.Infrastructure/Data), or AgencyPolicySeeder :270 / AgencyCodePolicySeeder :341 / AgencyProvisioningService.cs:167. No transaction. | No | Explicit transaction around `ApplyManifestAsync` (same as rows 1-2), or publish before the save in `RecomputeAllAsync`. |
| 5 | A | IAssetAssignmentApproved | src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs:216 | Caller `WorkflowInstancesController.Approve` (src/Services/HRCore/HR.Core.Web/Controllers/WorkflowInstancesController.cs:107). `ApproveAsync` saves at :117, then publishes at :120 → :179 → :216 and returns at :122. The controller returns at :116. No save, so the event is lost. Only reachable for `EntityType=AssetAssignment`. The other ApproveAsync callers (Requisitions, Offers, Competitions, EstablishmentChanges, SelectionCommitteeLifecycleService, RecruitmentAppealService) are typed to other entities. | No | Call `PublishWorkflowOutcomeAsync` for the asset types before the save at :117. The publish only needs instance fields, so state and event commit in one save. |
| 6 | A | IAssetLossApproved | WorkflowInstanceActionService.cs:245 | Same as row 5 (save :117 → publish :120 → :185 → :245 → return). Caller WorkflowInstancesController.cs:107. | No | Same as row 5. |
| 7 | A | IAssetAssignmentRejected | WorkflowInstanceActionService.cs:228 | Caller `WorkflowInstancesController.Reject` (WorkflowInstancesController.cs:123). `RejectAsync` saves at :164, then publishes at :166 → :179 → :228 and returns at :168. No save. | No | Publish before the save at :164. |
| 8 | A | IAssetLossRejected | WorkflowInstanceActionService.cs:256 | Same as row 7 (save :164 → publish :166 → :185 → :256 → return). Caller WorkflowInstancesController.cs:123. | No | Same as row 7. |
| 9 | A | IPolicyPublished (concrete `PolicyPublished`) | src/Services/HRCore/HR.Core.Web/Controllers/PoliciesController.cs:121 | `PoliciesController.Publish`: `PolicyVersionPublisher.PublishDraftAsync` at :117 saves (src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyVersionPublisher.cs:58), then the publish at :121, then return at :129. No save, so the event is lost. (`publish` is optional in the ctor at :25 but DI always supplies it.) | No | Publish inside `PublishDraftAsync` before its save at :58 (pass `IPublishEndpoint` into the publisher). This also covers the UI path, which never publishes (see note N3). |
| 10 | B | IEmployeeCreated | src/Services/HRCore/HR.Core.Infrastructure/Integration/EmployeeIntegrationService.cs:41 | Caller `EmployeesController.Create` (src/Services/HRCore/HR.Core.Web/Controllers/EmployeesController.cs:217, after `Add` at :216). `CreateEmployeeWithEventAsync`: save :17 (employee) → add history/record :19-39 → publish :41 → save :52. No transaction. | No (client Guid id) | Drop the save at :17: Add, add history/record, publish, one save at :52. The `AnyAsync` at :27 only reads, so no DB value is needed. |
| 11 | B | IEmployeeCreated | EmployeeIntegrationService.cs:41 | Caller `DataImportController.ImportEmployees` loop (src/Services/HRCore/HR.Core.Web/Controllers/DataImportController.cs:85). Same flow as row 10, once per CSV line. **Bug:** the caller never `Add`s the employee (:72-85), so the save at :17 saves nothing for it. The history/record rows and the event then reference an employee that is never persisted (FK failure at :52 expected, or an orphan event). | No | Add `db.Employees.Add(employee)` in the caller, then use the row 10 fix. Consider one save or transaction per line. |
| 12 | B | IEmployeeStatusChanged | EmployeeIntegrationService.cs:66 | Caller `EmployeeLifecycleController.Anonymize` (src/Services/HRCore/HR.Core.Web/Controllers/EmployeeLifecycleController.cs:61). Save :60 → wrapper publishes at :66 → wrapper saves at :75. | No | Remove the save at :60 and let the wrapper's save at :75 commit both. |
| 13 | B | IEmployeeStatusChanged | EmployeeIntegrationService.cs:66 | Caller `EmployeeLifecycleController.Merge` (:98). Save :97 → publish :66 → save :75. | No | Remove the save at :97. |
| 14 | B | IEmployeeStatusChanged | EmployeeIntegrationService.cs:66 | Caller `EmployeeLifecycleController.Unmerge` (:127). Save :126 → publish :66 → save :75. | No | Remove the save at :126. |
| 15 | OK | IEmployeeStatusChanged | EmployeeIntegrationService.cs:66 | Caller `EmployeesController.Edit` status branch (EmployeesController.cs:410). Changes at :369-408 → publish :66 → single save :75. (The non-status branch saves at :419 with no event.) | No | None |
| 16 | C | IEmployeeStatusChanged | EmployeeIntegrationService.cs:66 | Caller `SeparationCompletedConsumer.Consume` (src/Services/HRCore/HR.Core.Infrastructure/Consumers/SeparationCompletedConsumer.cs:79). The consumer saves at :75, then the wrapper publishes at :66. Inside a consumer scope the injected `IPublishEndpoint` is the ConsumeContext, so the message goes straight to the broker. The wrapper then saves again at :75. | No | Consumer outbox (`UseEntityFrameworkOutbox<HRCoreDbContext>` on endpoints). Then drop the consumer's save at :75 or keep a single save. |
| 17 | B | IOrgUnitChanged | src/Services/HRCore/HR.Core.Infrastructure/Integration/OrgUnitIntegrationService.cs:13 | Caller `OrgUnitsController.Create` (src/Services/HRCore/HR.Core.Web/Controllers/OrgUnitsController.cs:191). Saves before the publish: :187, `AssignPathOnCreateAsync` (OrgUnitPathService.cs:121), `AppendVersionAsync` (src/Services/HRCore/HR.Core.Infrastructure/Services/OrgStructure/OrgUnitVersionService.cs:88) at :190. Then publish :191 and save :192. No transaction. | The `Code` comes from `CodeGeneratorService` (raw SQL `INSERT … ON CONFLICT … RETURNING` at src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs:349-368, plus a `RecordIssuanceAsync` save at :419) at :168, **before** the entity exists, so it is already known. The path needs only the parent path (read) and the client id. | Explicit transaction from :186 to :192 (the code generation can stay outside). Or restructure: AssignPath/AppendVersion without their own saves, then publish, then one save. |
| 18 | B | IOrgUnitChanged (subtree) | OrgUnitIntegrationService.cs:13 via `PublishManyOrgUnitsChangedAsync` :25-29 | Caller `OrgUnitsController.Edit` move branch (:267). Save :257, then `MoveSubtreeAsync`, which runs its **own** explicit transaction (OrgUnitPathService.cs:142, save :189, commit :191). Then publish :267, save :268, then `AppendVersionAsync` save (OrgUnitVersionService.cs:88) at :270. The publish is outside the existing transaction. | No | Publish inside `MoveSubtreeAsync` before :189 (pass in the integration service), or use one caller-level transaction. **Caution:** `MoveSubtreeAsync` calls `BeginTransactionAsync` itself, which throws inside an outer transaction, so it must be made transaction-aware. |
| 19 | B | IOrgUnitChanged | OrgUnitIntegrationService.cs:13 | Caller `OrgUnitsController.Edit` non-move branch (:276). Save :274 → publish :276 → save :277, then the AppendVersion save (OrgUnitVersionService.cs:88) at :280. | No | Drop the save at :274 (publish, then one save at :277), or move AppendVersion before the single save. |
| 20 | OK | IOrgUnitChanged (deleted) | OrgUnitIntegrationService.cs:13 | Caller `OrgUnitsController.Delete` (:307). Publish :307 → Remove :308 → single save :309. | No | None |
| 21 | B | IOrgUnitChanged (subtree) | OrgUnitIntegrationService.cs:13 via `PublishManyOrgUnitsChangedAsync` | Caller `OrgStructureApiController.UpdateUnit` move branch (src/Services/HRCore/HR.Core.Web/Controllers/OrgStructureApiController.cs:66). Save :62 → `MoveSubtreeAsync` (own transaction, OrgUnitPathService.cs:142/189/191) → publish :66 → save :67 → AppendVersion save (OrgUnitVersionService.cs:88) at :69. | No | Same as row 18. |
| 22 | B | IOrgUnitChanged | OrgUnitIntegrationService.cs:13 | Caller `OrgStructureApiController.UpdateUnit` non-move branch (:75). Save :73 → publish :75 → save :76, then AppendVersion at :80. | No | Same as row 19. |
| 23 | OK | ITransactionRegisteredInDiwan | src/Services/HRCore/HR.Core.Infrastructure/Integration/DiwanIntegrationService.cs:17 | Caller `DiwanCorrespondencesController.Create` (src/Services/HRCore/HR.Core.Web/Controllers/OrgBranchControllers.cs:396). Add :13 → publish :17 (only when `ServiceTransactionId` is set, which this caller never sets, so in practice no event) → single save :27. | No | None |
| 24 | C | ITransactionRegisteredInDiwan | DiwanIntegrationService.cs:17 | Caller `HireCaseCompletedDiwanConsumer.Consume` (src/Services/HRCore/HR.Core.Infrastructure/Consumers/HireCaseCompletedDiwanConsumer.cs:54). The consumer saves the activation at :46, then the wrapper adds and publishes (direct to broker) and saves at :27. | No | Consumer outbox. Then one save (drop :46 or keep it inside the outbox transaction). |
| 25 | OK | IAttendancePeriodClosed (×N employees) | src/Services/HRCore/HR.Core.Infrastructure/Integration/AttendanceIntegrationService.cs:104 | Caller `AttendanceController.ClosePeriod` (OrgBranchControllers.cs:270). Publish loop :88-120 → single save :122. No state change, only outbox rows. | No | None (large N gives one large save; acceptable). |
| 26 | OK | ILeaveRequested | src/Services/HRCore/HR.Core.Web/Controllers/EssPortalController.cs:103 | Publish :103 → save :120 (outbox only, no state). | No | None |
| 27 | OK | IAdvanceRequested | EssPortalController.cs:146 | Publish :146 → save :158 (outbox only). | No | None |
| 28 | B | IExportJobRequested | src/Services/HRCore/HR.Core.Infrastructure/ExportJobs/HRCoreExportJobService.cs:90 | Caller `ReportsController` (src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs:95). `EnqueueAsync`: Add :87 → save :88 → publish :90 → save :98. | No (client `job.Id`) | Remove the save at :88: Add, publish, one save. |
| 29 | B | IExportJobRequested | HRCoreExportJobService.cs:90 | Caller `RecruitmentReportsController` (src/Services/HRCore/HR.Core.Web/Controllers/RecruitmentReportsController.cs:191). Same as row 28. | No | Same as row 28. |
| 30 | OK | ICandidateAccepted | src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentHireClosureService.cs:512 | Inside the **existing explicit transaction** :158 → (save :247, new employee) → publish :321→:512 → save :336 → commit :337. On failure, `ChangeTracker.Clear()` runs at :124/:133. Caller is `RecruitmentIntegrationService` (web). | No | None |
| 31 | OK | IEmployeeCreated (hire) | RecruitmentHireClosureService.cs:536 | Same transaction as row 30. | No | None |
| 32 | OK | IInternalPlacementCompleted | RecruitmentHireClosureService.cs:556 | Same transaction as row 30 (internal mobility branch). | No | None |
| 33 | OK | IRecruitmentRequisitionClosed | src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/Analytics/RecruitmentRequisitionClosedEvent.cs:50 (`StageAsync`) | Caller RecruitmentHireClosureService.cs:334, inside the transaction from row 30 (save :336, commit :337). | No | None |
| 34 | OK | IRecruitmentRequisitionClosed | RecruitmentRequisitionClosedEvent.cs:50 | Caller `RecruitmentRequisitionLifecycleService.CloseAsync` (src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentRequisitionLifecycleService.cs:327). Mutations :317-325 (the cascade helpers do not save) → stage :327 → single save :329. Web caller RecruitmentRequisitionsController.cs:477. | No | None |
| 35 | OK | IRecruitmentRequisitionClosed | RecruitmentRequisitionClosedEvent.cs:50 | Caller `RecruitmentRequisitionLifecycleService.CancelAsync` (:373). Cascade :371 → stage :373 → single save :374. Web caller RecruitmentRequisitionsController.cs:494. | No | None |
| 36 | OK | IRecruitmentRequisitionClosed (×blocking requisitions) | RecruitmentRequisitionClosedEvent.cs:50 | Caller `EstablishmentChangeApplier.ApplyFreezeFundedPositionAsync` (src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/EstablishmentChangeApplier.cs:501). Freeze + cascade :492-502 → single save :504. Reached via EstablishmentChangeLifecycleService.cs:175 ← EstablishmentChangeWorkflowHandler.cs:28 ← WorkflowInstanceActionService.cs:202, which runs after the instance save :117. The request's own "Applied" save at EstablishmentChangeLifecycleService.cs:200 is separate, but the event is atomic with the requisition cancellation it describes. | No | None (optional: one transaction for the whole approve → apply chain) |
| 37 | C | ISeparationPartyCompleted | src/Services/HRCore/HR.Core.Infrastructure/Consumers/ProcessPartyAckConsumers.cs:43 | `context.Publish`, no state change. | No | Consumer outbox |
| 38 | C | IHirePartyCompleted (compensate) | ProcessPartyAckConsumers.cs:63 | `CompensateHireAsync` saves at :163 (vacate position, revert application), then `context.Publish` at :63 goes straight to the broker. | No | Consumer outbox (publish and compensation commit together) |
| 39 | C | IHirePartyFaulted | ProcessPartyAckConsumers.cs:87 | `context.Publish`, no state change. | No | Consumer outbox |
| 40 | C | IHirePartyCompleted (ack) | ProcessPartyAckConsumers.cs:100 | `context.Publish`, no state change. | No | Consumer outbox |
| 41 | C | ITransferPartyCompleted | ProcessPartyAckConsumers.cs:117 | `context.Publish`, no state change. | No | Consumer outbox |
| 42 | B | IStartTransferSaga | src/Services/HRMovements/HR.Movements.Web/Controllers/TransferRequestsController.cs:390 | `Execute` ([Idempotent]): save :345 (regime snapshot) → **`ExecuteUpdateAsync` :350-354** (Approved→Executed compare-and-set, auto-committed immediately, outside the change tracker) → add execution record/impact/checklist :362-385 → save :387 → publish :390 → save :407. No transaction. | **Yes.** The `ExecuteUpdate` row count (`transitioned`, :356) is the concurrency guard and runs as its own statement. One save cannot replace it, so the fix needs an explicit transaction. | Explicit transaction from :350 to :407 (ExecuteUpdate + adds + publish + one save + commit). Publish before the save at :387 and drop :407. |
| 43 | OK | ICancelTransferSaga | TransferRequestsController.cs:432 | `Cancel` post-execute branch: soft-cancel :431 → publish :432 → single save :439. | No | None |
| 44 | OK | IResumeTransferSaga | src/Services/HRMovements/HR.Movements.Web/Controllers/ProcessInterventionsController.cs:45 | Publish :45 → status change :64-65 → single save :66. | No | None |
| 45 | OK | IResumeSecondmentSaga | ProcessInterventionsController.cs:54 | Publish :54 → :64-65 → save :66. | No | None |
| 46 | OK | ICancelTransferSaga | ProcessInterventionsController.cs:82 | Publish :82 → :103-104 → save :105. | No | None |
| 47 | OK | ICancelSecondmentSaga | ProcessInterventionsController.cs:92 | Publish :92 → :103-104 → save :105. | No | None |
| 48 | B | IStartSecondmentSaga | src/Services/HRMovements/HR.Movements.Web/Controllers/MovementsExtendedControllers.cs:436 | `SecondmentRequestsController.Activate`: state :421-433 → save :434 → publish :436 → save :446. | No | Remove the save at :434. |
| 49 | OK | ICancelSecondmentSaga | MovementsExtendedControllers.cs:475 | `SecondmentRequestsController.Close`: state :468-472 → publish :475 → single save :482. | No | None |
| 50 | OK | ICancelSecondmentSaga | MovementsExtendedControllers.cs:510 | `Cancel` (active, pre-effective): soft-cancel :509 → publish :510 → save :517. | No | None |
| 51 | OK | ICancelSecondmentSaga | MovementsExtendedControllers.cs:525 | `Cancel` (active): soft-cancel :524 → publish :525 → save :532. | No | None |
| 52 | B | ILoanAssignmentExecuted | MovementsExtendedControllers.cs:784 | `LoanAssignmentRequestsController.Execute`: state :769-781 → save :782 → publish :784 → save :795. | No | Remove the save at :782. |
| 53 | OK | ILoanAssignmentEnded | MovementsExtendedControllers.cs:824 | `LoanAssignmentRequestsController.Close`: state :818-822 → publish :824 → single save :834. | No | None |
| 54 | OK | ILoanAssignmentEnded | MovementsExtendedControllers.cs:864 | `LoanAssignmentRequestsController.Cancel` (active): :862-863 → publish :864 → single save :874. | No | None |
| 55 | A | IPromotionExecuted | MovementsExtendedControllers.cs:1034 | `PromotionRequestsController.Execute`: state :1021-1031 → save :1032 → publish :1034 → return :1049. No save, so the event is lost. | No | Publish before the save (move :1034-1047 above :1032). |
| 56 | A | IDelegationExecuted | MovementsExtendedControllers.cs:1191 | `DelegationRequestsController.Execute`: state :1178-1188 → save :1189 → publish :1191 → return :1202. No save, so the event is lost. | No | Publish before the save at :1189. |
| 57 | C | IExecuteTransferParty (dispatch) | src/Services/HRMovements/HR.Movements.Infrastructure/Sagas/TransferSecondmentStateMachines.cs:226 | `TransferStateMachine.Dispatch` (`ctx.Publish` per party). Reached from `Started` :103 and `Resume` :155. The saga state is persisted by the EF repository (pessimistic, `ExistingDbContext<MovementsDbContext>`, HR.Movements.Web/Program.cs:50-61) **after** the broker send. | No | Consumer/saga outbox (`UseEntityFrameworkOutbox<MovementsDbContext>` on the saga endpoints: same DbContext, so saga row and outbox commit together). |
| 58 | C | IExecuteTransferParty (compensate) | TransferSecondmentStateMachines.cs:253 | `StartCompensation`, from `Cancel` :147/:163. | No | Same as row 57 |
| 59 | C | ITransferExecuted | TransferSecondmentStateMachines.cs:273 | `PublishFinal`, from the last `PartyCompleted` :122. | No | Same as row 57 |
| 60 | C | IProcessInterventionRequested (transfer) | TransferSecondmentStateMachines.cs:295 | `OpenIntervention`, from `PartyFaulted` :134, timeout :143, compensation fault :188. | No | Same as row 57 |
| 61 | C | TransferPartyTimeoutMsg (scheduled) | TransferSecondmentStateMachines.cs:104, :156 (`.Schedule`; `.Unschedule` :121/:133/:146) | Delayed-message scheduler (`UseDelayedMessageScheduler`, MassTransitExtensions.cs:79). Sent from the saga ConsumeContext. | No | Same as row 57 |
| 62 | C | IExecuteSecondmentParty (dispatch) | TransferSecondmentStateMachines.cs:503 | `SecondmentStateMachine.Dispatch`, from `Started` :382 and `Resume` :430. | No | Same as row 57 |
| 63 | C | IExecuteSecondmentParty (compensate) | TransferSecondmentStateMachines.cs:525 | `StartCompensation`, from `Cancel` :422/:438/:464. | No | Same as row 57 |
| 64 | C | ISecondmentExecuted | TransferSecondmentStateMachines.cs:540 | `PublishFinal`, from :397. | No | Same as row 57 |
| 65 | C | IProcessInterventionRequested (secondment) | TransferSecondmentStateMachines.cs:554 | `OpenIntervention`, from :409, :418, :459. | No | Same as row 57 |
| 66 | C | SecondmentPartyTimeoutMsg (scheduled) | TransferSecondmentStateMachines.cs:383, :431 (`.Schedule`; `.Unschedule` :396/:408/:421) | Same as row 61. | No | Same as row 57 |

**Totals (66 rows):** A = 10 (rows 1, 2, 3, 5, 6, 7, 8, 9, 55, 56). B = 16 (rows 4, 10, 11, 12, 13, 14, 17, 18, 19, 21, 22, 28, 29, 42, 48, 52). C = 17 (rows 16, 24, 37-41, 57-66). OK = 23.

### Wrappers found (publish inside; the caller decides)

- `EmployeeIntegrationService.CreateEmployeeWithEventAsync` (EmployeeIntegrationService.cs:15). Internally save → publish → save (B). Callers: EmployeesController.cs:217, DataImportController.cs:85.
- `EmployeeIntegrationService.PublishStatusChangedIfNeededAsync` (:56). Publish, then its own save at :75. Callers: EmployeeLifecycleController.cs:61/98/127, EmployeesController.cs:410, SeparationCompletedConsumer.cs:79.
- `OrgUnitIntegrationService.PublishOrgUnitChangedAsync` / `PublishManyOrgUnitsChangedAsync` / `PublishAllOrgUnitsAsync` (:11/:25/:31). None of them saves. Callers: OrgUnitsController.cs:191/267/276/307, OrgStructureApiController.cs:66/75, Program.cs:321, AgencyOrgStructureProvisioner.cs:76.
- `DiwanIntegrationService.RegisterCorrespondenceAsync` (:11). Add + publish + save at :27. Callers: OrgBranchControllers.cs:396, HireCaseCompletedDiwanConsumer.cs:54.
- `AttendanceIntegrationService.ClosePeriodAndPublishAsync` (:75). Publish loop + save at :122. Caller: OrgBranchControllers.cs:270.
- `RecruitmentRequisitionClosedEvent.StageAsync` (:45). No save. Callers: RecruitmentHireClosureService.cs:334, RecruitmentRequisitionLifecycleService.cs:327/373, EstablishmentChangeApplier.cs:501.
- `HRCoreExportJobService.EnqueueAsync` (:49). Save → publish → save (B). Callers: ReportsController.cs:95, RecruitmentReportsController.cs:191.
- `AgencyProvisioningService.ApplyManifestAsync` (:43) → `AgencyOrgStructureProvisioner.ApplyAsync` (:16). Callers: OnboardingController.cs:20/32.
- `WorkflowInstanceActionService.PublishWorkflowOutcomeAsync` (:171) → `PublishAssetAssignmentOutcomeAsync` (:208) / `PublishAssetLossOutcomeAsync` (:240). Always called after the save. Asset outcomes are reachable via WorkflowInstancesController.cs:107/123.
- `RecruitmentHireClosureService.PublishHireEventsAsync` (:480, private), inside the transaction. `RecruitmentIntegrationService` is a thin pass-through to the closure service.

### Notes

- N1 (DB-generated value): only row 42 (`ExecuteUpdateAsync` compare-and-set at TransferRequestsController.cs:350-354) truly needs an explicit transaction. In row 17, the OrgUnit `Code` comes from a raw-SQL sequence upsert (CodeGeneratorService.cs:349-368), but it is obtained before the entity is added, so it does not force two saves.
- N2 (existing explicit transactions): the hire closure (RecruitmentHireClosureService.cs:158-337) already publishes inside one transaction (OK). `OrgUnitPathService.MoveSubtreeAsync` (:141-191) opens its own transaction and commits before the caller publishes (rows 18 and 21). A caller-level transaction would break its unconditional `BeginTransactionAsync`. `CodeGeneratorService` (:297) opens a Serializable transaction on non-Postgres relational providers. `EstablishmentChangeApplier` (:191-235) opens one for bulk catalog creation, which does not publish.
- N3 (missing events, not publish sites): `PolicyTemplatesController.PublishFromUi` (src/Services/HRCore/HR.Core.Web/Controllers/PolicyTemplatesController.cs:194) publishes a policy version with no `IPolicyPublished`. `DataImportController.ImportOrgUnits` (:20-46) creates org units with no `IOrgUnitChanged` and no path.
- N4 (hosted/startup): the only startup publish is Program.cs:321 (row 3, lost). No `BackgroundService` publishes.
- N5: `DataImportController.ImportEmployees` never adds the employee (row 11). That is a functional bug, independent of atomicity.

### Missing from EVENT_PUBLISHER_INVENTORY.md

Event types published in scope that docs/EVENT_PUBLISHER_INVENTORY.md does not list:

- HR.Core: `IAgencyOnboarded`, `IPolicyPublished` (`PolicyPublished`), `IExportJobRequested`, `IAssetAssignmentApproved`, `IAssetAssignmentRejected`, `IAssetLossApproved`, `IAssetLossRejected`, `IInternalPlacementCompleted`, `IRecruitmentRequisitionClosed`, `ISeparationPartyCompleted`, `IHirePartyCompleted`, `IHirePartyFaulted`, `ITransferPartyCompleted`. `IEmployeeCreated` from `RecruitmentHireClosureService` is listed only under `EmployeeIntegrationService`. `IOrgUnitChanged` from onboarding (`AgencyOrgStructureProvisioner`) is also missing.
- HR.Movements: `IPromotionExecuted`, `IDelegationExecuted`, `ILoanAssignmentEnded`, `IStartTransferSaga`, `ICancelTransferSaga`, `IResumeTransferSaga`, `IStartSecondmentSaga`, `ICancelSecondmentSaga`, `IResumeSecondmentSaga`, `IExecuteTransferParty`, `IExecuteSecondmentParty`, `IProcessInterventionRequested`, and the scheduled `TransferPartyTimeoutMsg` / `SecondmentPartyTimeoutMsg`.
- Inaccuracies in the doc: `ITransferExecuted` and `ISecondmentExecuted` are attributed to `TransferRequestsController` and `SecondmentRequestsController.Activate`. Those controllers actually publish `IStartTransferSaga` / `IStartSecondmentSaga`; the `*Executed` events come from the saga `PublishFinal` (class C). The "Startup sync" row for `PublishAllOrgUnitsAsync` hides that the events are never saved (row 3). The doc's "Save → Publish → Save" pattern it recommends is itself class B.

---

## Group 2: HR.Contracts, Payroll, Compliance, HR.Health, HR.Training, IDCard, Records.Archive, Dev.Analytics, Admin.Logistics, Auth.Directory, IT.Infra, File.Storage, Shared

Scope: HRContracts, Payroll, Compliance, HRHealth, HRTraining, IDCard, RecordsArchive, DevAnalytics, AdminLogistics,
AuthDirectory, ITInfrastructure, FileStorage, src/Shared. (HRCore / HRMovements: other inventory; only the two HRMovements
subclasses of the shared party-ack base are noted.) Read-only inventory against the working tree of 2026-11-02.

Notes that apply to many rows:

- Outbox services (bus outbox, `MassTransitExtensions.cs:120-129`): HRContracts, Payroll, Compliance, HRHealth, HRTraining, IDCard.
  No-outbox: RecordsArchive, DevAnalytics, AdminLogistics, AuthDirectory, ITInfrastructure, FileStorage (FileStorage has a bus but no publish site).
- Every service registers `DbContext` as an alias of its own context (e.g. `HR.Contracts.Web/Program.cs:38`), so the
  `InboxConsumeFilter` (`src/Shared/HRManagement.ServiceDefaults/IdempotentConsumerFilter.cs:63`) wraps every consumer and
  saga in one explicit DB transaction, committed at `:81-82` after the consumer returns. A `ConsumeContext.Publish` (and an
  `IPublishEndpoint` resolved inside a consume scope) goes straight to the broker before that commit.
- No consumer or saga in scope opens its own `BeginTransaction`. The only transactions besides the inbox filter are
  `PayrollRepository.cs:127/201` (salary-line packages; join an ambient tx when present; no publish), `FilePlatformService.cs`
  (no publish) and seeders.
- No MediatR pipeline behaviour opens a transaction (only `ValidationBehavior` in HRContracts/Payroll).
- `IContractsRepository.SaveChangesAsync` / `IPayrollRepository.SaveChangesAsync` are plain `db.SaveChangesAsync`
  (`ContractsRepository.cs:54`, `PayrollRepository.cs:103`).
- Reference numbers (`ServiceTransactionReferenceSupport.ApplyReferenceAsync`, ID-card serials, archive refs) come from the
  code-generation service over HTTP (`ICodeGenerationClient`), obtained *before* the first save — not a DB-generated value.

### Publish sites

| # | Service | Class | Event | Publish site (file:line) | Flow (save/publish order with file:line of each save, or the caller for wrappers) | DB value needed? | Proposed fix |
|---|---------|-------|-------|--------------------------|-------------------------------------------------------------------------------------|------------------|--------------|
| 1 | HRContracts | B | IJobUpdateApplied | src/Services/HRContracts/HR.Contracts.Infrastructure/Messaging/MassTransitEventPublisher.cs:195 (wrapper `PublishJobUpdateAppliedAsync`) | Caller `CreateAdministrativeDecisionHandler` src/Services/HRContracts/HR.Contracts.Application/Features/AdministrativeDecisions/AdministrativeDecisionHandlers.cs:64. Save :61 → publish :64 → save :72. Entry: AdministrativeDecisionsController.cs:97 | No (reference from code-gen HTTP before :61) | Drop save :61; one save after publish |
| 2 | HRContracts | B | IAdvanceApproved | MassTransitEventPublisher.cs:165 (`PublishAdvanceApprovedAsync`) | Caller `ApproveAdvanceRequestHandler` src/Services/HRContracts/HR.Contracts.Application/Features/AdvanceRequests/AdvanceRequestHandlers.cs:171. Save :170 → publish → save :181. Entries: AdvanceRequestsController.cs:152, :186 | No (installment plan built client-side :154) | Drop save :170 |
| 3 | HRContracts | B | ICommencementRecorded | MassTransitEventPublisher.cs:59 (`PublishCommencementRecordedAsync`) | Caller `CreateCommencementRecordHandler` src/Services/HRContracts/HR.Contracts.Application/Features/CommencementRecords/CommencementRecordHandlers.cs:62. Save :56 → `HireCaseSupport.MarkContractClassifiedAsync` (saves HireCaseSupport.cs:37 and :96) → publish :62 → save :64. Entry: CommencementRecordsController.cs:101 | No | Explicit transaction over the whole handler (helpers save internally), or make HireCaseSupport helpers non-saving and keep one save :64 |
| 4 | HRContracts | B | IContractActivated | MassTransitEventPublisher.cs:29 (`PublishContractActivatedAsync`) | Caller `ActivateContractHandler` src/Services/HRContracts/HR.Contracts.Application/Features/Contracts/ActivateContractCommands.cs:39. Save :37 → publish :39 → `MarkPayrollLinkedAsync` (save HireCaseSupport.cs:96, which already flushes the outbox row) → save :41. Entry: ContractsController.cs:188 | No | Drop save :37 and make MarkPayrollLinkedAsync non-saving (or explicit tx) |
| 5 | HRContracts | B (ExecuteUpdate) | IStartHireSaga (command) | MassTransitEventPublisher.cs:341 (`StartHireSagaAsync`) | Caller `PublishHireCaseHandler` src/Services/HRContracts/HR.Contracts.Application/Features/HireCases/HireCaseHandlers.cs:109. `ExecuteUpdateAsync` :93 and :102 (autocommit, state → PostProcessing/Completed) → publish :109 → save :115 (outbox row only). Entry: HireWizardController.cs:346 | Set-based ExecuteUpdate (no generated value, but cannot be folded into SaveChanges as is) | Explicit `BeginTransactionAsync` around the two ExecuteUpdates + publish + save (or set State on the tracked `transaction` loaded at :65 and use one save) |
| 6 | HRContracts | C (via wrapper) | IProcessInterventionRequested | MassTransitEventPublisher.cs:359 (`PublishProcessInterventionRequestedAsync`) | Caller `ProcessCandidateAcceptedHandler.FlagUnresolvedContractTypeAsync` src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs:164, sent by `CandidateAcceptedConsumer` src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs:30. Save :124 → seed/mark saves (HireCaseSupport.cs:37, :96) → publish :164 → save :131; all inside the inbox tx, but the `IPublishEndpoint` in a consume scope is the ConsumeContext → broker before commit | No | Consumer outbox (`UseEntityFrameworkOutbox<ContractsDbContext>` on receive endpoints) |
| 7 | HRContracts | C (via wrapper) | IProcessInterventionRequested | same as #6 | Same handler sent by `EmployeeOfferAcceptedConsumer` InboundEventConsumers.cs:51 (v-less message: LegalRegimeId/EmploymentType null → unresolved path more likely) | No | as #6 |
| 8 | HRContracts | B (ExecuteUpdate) | ILeaveCancelled | MassTransitEventPublisher.cs:146 (`PublishLeaveCancelledAsync`) | Caller `CancelLeaveRequestHandler` src/Services/HRContracts/HR.Contracts.Application/Features/LeaveRequests/LeaveRequestHandlers.cs:200. ExecuteUpdate claim :172 → `ReloadAsync` :182 → ExecuteUpdate balance credit :223-229 → save :197 → publish :200 → save :211. Entry: LeaveRequestsController.cs:151 | Yes-ish: claim row count (:178) decides the outcome and the xmin token is re-read (:182) — cannot collapse to one SaveChanges | Explicit transaction from the claim through the final save |
| 9 | HRContracts | B (ExecuteUpdate) | ILeaveApproved | MassTransitEventPublisher.cs:105 (`PublishLeaveApprovedAsync`) | Caller `ApproveLeaveRequestHandler` LeaveRequestHandlers.cs:342. ExecuteUpdate claim :284 → Reload :301 → ExecuteUpdate debit :308 (rows==0 → revert ExecuteUpdate :365) → save :341 → publish :342 → save :353. Entries: LeaveRequestsController.cs:183 (`[Idempotent]`, whose filter does one more SaveChanges after the action), :219 | Yes-ish: claim/debit row counts + xmin reload | Explicit transaction from the claim through the final save (also removes the manual revert/credit-back compensation) |
| 10 | HRContracts | B | IConfirmationApproved | MassTransitEventPublisher.cs:81 (`PublishConfirmationApprovedAsync`) | Caller `CreateProbationRecordHandler` src/Services/HRContracts/HR.Contracts.Application/Features/ProbationRecords/ProbationRecordHandlers.cs:73. Save :72 → publish → save :80. Entry: ProbationRecordsController.cs:96 | No | Drop save :72 |
| 11 | HRContracts | B (benign) | IStartSeparationSaga (command) | MassTransitEventPublisher.cs:309 (`StartSeparationSagaAsync`) | Caller `PublishSeparationHandler` src/Services/HRContracts/HR.Contracts.Application/Features/SeparationRecords/SeparationRecordHandlers.cs:190. Conditional save :184 (only the regime snapshot, when SourceLegalRegimeId empty) → publish :190 → `SeparationEventPublished=true` :200 + tx move :212-217 → save :220. The announced state commits with the outbox row; when the snapshot already exists the path is OK. `SeedChecklistAsync` (SeparationRecordSupport.cs:11) does not save. Entries: SeparationRecordsController.cs:169, SeparationWizardController.cs:156 | No | Drop save :184 |
| 12 | HRContracts | B | ISeparationStepCompleted | MassTransitEventPublisher.cs:271 (`PublishSeparationStepCompletedAsync`) | Caller `CompleteSeparationChecklistStepHandler` SeparationRecordHandlers.cs:262. (TerminateLinkedContractAsync :252 does not save) save :260 → publish → save :268. Entry: SeparationWizardController.cs:108 | No | Drop save :260 |
| 13 | HRContracts | OK | ICancelSeparationSaga (command) | MassTransitEventPublisher.cs:327 (`CancelSeparationSagaAsync`) | Caller `CancelSeparationHandler` SeparationRecordHandlers.cs:326: SoftCancel :322 → publish :326 → tx move → one save :339. Entry: SeparationRecordsController.cs:185 | No | — |
| 14 | HRContracts | OK | ICancelSeparationSaga (command) | MassTransitEventPublisher.cs:327 | `DeleteSeparationRecordHandler` SeparationRecordHandlers.cs:124 → `new CancelSeparationHandler(...).Handle` (same flow as #13, save :339). Entry: SeparationRecordsController.cs:154 | No | — |
| 15 | HRContracts | OK (hosted service) | IProcessInterventionRequested | MassTransitEventPublisher.cs:359 | Caller `HireCommencementWatcher.FlagOverdueAsync` src/Services/HRContracts/HR.Contracts.Infrastructure/Services/HireCommencementWatcher.cs:78 run by BackgroundService `HireCommencementWatchJob` (:99, scope :121, session advisory lock — no tx). Set `CommencementOverdueAt` :77 → publish (loop) → one save :91 | No | — (only hosted-service publish in scope; relies on the bus outbox resolving in a non-request DI scope, which it does) |
| 16 | HRContracts | OK | IResumeSeparationSaga | src/Services/HRContracts/HR.Contracts.Web/Controllers/ProcessInterventionsController.cs:66 | publish → status Resolved :85 → save :87 | No | — |
| 17 | HRContracts | OK | IResumeHireSaga | ProcessInterventionsController.cs:75 | publish → save :87 | No | — |
| 18 | HRContracts | OK | ICancelSeparationSaga | ProcessInterventionsController.cs:101 | publish → status Cancelled :122 → save :125 | No | — |
| 19 | HRContracts | OK | ICancelHireSaga | ProcessInterventionsController.cs:111 | publish → save :125 | No | — |
| 20 | HRContracts | B | IExportJobRequested | src/Services/HRContracts/HR.Contracts.Infrastructure/ExportJobs/ContractsExportJobService.cs:75 | `EnqueueAsync`: add job :72 → save :73 → publish :75 → save :83. Entries: HR.Contracts.Web/Controllers/ExportController.cs:30, :49, :82, :101 | No (job Id client-generated) | Drop save :73 |
| 21 | HRContracts | — (dead) | ITransactionArchived | MassTransitEventPublisher.cs:221 (`PublishTransactionArchivedAsync`) | No caller in src/ (archive event is published by RecordsArchive, #45-46) | — | Remove from IEventPublisher |
| 22 | HRContracts | — (dead) | ISeparationCompleted | MassTransitEventPublisher.cs:243 (`PublishSeparationCompletedAsync`) | No caller (published by the saga, #29) | — | Remove |
| 23 | HRContracts | — (dead) | IHireCaseCompleted | MassTransitEventPublisher.cs:288 (`PublishHireCaseCompletedAsync`) | No caller (published by the saga, #25) | — | Remove |
| 24 | HRContracts | C (saga) | IExecuteHireParty | src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/HireCaseStateMachine.cs:219 (`DispatchPending`, on Started :90 and Resume :147) | `ctx.Publish` → broker; saga row (EF repo, pessimistic, Program.cs:72-75) + inbox commit afterwards | No | Endpoint outbox for saga endpoints |
| 25 | HRContracts | C (saga) | IHireCaseCompleted | HireCaseStateMachine.cs:258 (`PublishFinal`, :109) | ctx.Publish before saga state commit | No | as #24 |
| 26 | HRContracts | C (saga) | IExecuteHireParty (compensate) | HireCaseStateMachine.cs:242 (`StartCompensation`, :134, :155) | as #24 | No | as #24 |
| 27 | HRContracts | C (saga) | IProcessInterventionRequested | HireCaseStateMachine.cs:273 (`OpenIntervention`, :121, :130, :180) | as #24 | No | as #24 |
| 28 | HRContracts | C (saga, scheduled send) | HirePartyTimeoutMsg | HireCaseStateMachine.cs:91, :148 (`.Schedule`, delayed message scheduler) | scheduled before saga commit; Unschedule :108/:120/:133 likewise | No | as #24 |
| 29 | HRContracts | C (saga) | ISeparationCompleted | src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs:263 (`PublishFinalCompleted`, :129) | as #24 | No | as #24 |
| 30 | HRContracts | C (saga) | ISeparationSagaFinalized | SeparationStateMachine.cs:278 | as #24 | No | as #24 |
| 31 | HRContracts | C (saga) | IExecuteSeparationParty | SeparationStateMachine.cs:243 (`DispatchPendingParties`, :107, :170) | as #24 | No | as #24 |
| 32 | HRContracts | C (saga) | IExecuteSeparationParty (compensate) | SeparationStateMachine.cs:306 (`StartCompensation`, :157, :178) | as #24 | No | as #24 |
| 33 | HRContracts | C (saga) | IProcessInterventionRequested | SeparationStateMachine.cs:287 (`OpenIntervention`, :142, :153, :203) | as #24 | No | as #24 |
| 34 | HRContracts | C (saga, scheduled send) | SeparationPartyTimeoutMsg | SeparationStateMachine.cs:108, :171 | as #28 | No | as #24 |
| 35 | HRContracts | C | ISeparationPartyCompleted (compensate) | src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs:139 (`ContractsSeparationPartyConsumer`) | save :138 → context.Publish :139 (inside inbox tx) | No | Consumer outbox |
| 36 | HRContracts | C | ISeparationPartyCompleted (ack) | SagaSupportConsumers.cs:152 | context.Publish :152 → save :161 (nothing tracked) | No | Consumer outbox |
| 37 | Payroll | B | IPayrollProcessed | src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollEventPublisher.cs:15 (wrapper `PublishPayrollProcessedAsync`) | Caller `PostPayrollRunHandler` src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs:262. run.Post + PaymentBatch add → save :260 → publish :262 → save :264. Entries: PayrollApiController.cs:74, PayrollRunsController.cs:307 | No (BankTransferReference built client-side :251; ids client-generated) | Drop save :260 |
| 38 | Payroll | B | IExportJobRequested | src/Services/Payroll/Payroll.Infrastructure/ExportJobs/PayrollExportJobService.cs:75 | save :73 → publish → save :83. Entries: PayrollReportsController.cs:114, :159, :231, :332, :351 | No | Drop save :73 |
| 39 | Payroll | C | ISeparationPartyCompleted / ISeparationPartyFaulted | src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs:68 / :80 (`SeparationPartyConsumer`) | mediator `HandleSeparationCompletedCommand` (saves) or `VoidDraftEosAsync` save :103 → re-reads EOS id :59-64 → publish; Faulted published in catch then rethrow | Reads the EOS id back from DB for the evidence ref (client-generated Guid; could be returned by the command) | Consumer outbox |
| 40 | Payroll | C | ITransferPartyCompleted / ITransferPartyFaulted | PayrollEventConsumers.cs:126 / :138 (`TransferPartyConsumer`) | mediator command (saves) → publish; Faulted then rethrow | No | Consumer outbox |
| 41 | Payroll | C | IHirePartyCompleted | PayrollEventConsumers.cs:161 (`HirePartyConsumer`) | publish only, no state | No | Consumer outbox (or accept) |
| 42 | Payroll | C | ISecondmentPartyCompleted / ISecondmentPartyFaulted | PayrollEventConsumers.cs:190 / :202 (`SecondmentPartyConsumer`) | mediator command (saves) → publish; Faulted then rethrow | No | Consumer outbox |
| 43 | Compliance | B | IComplaintResolved | src/Services/Compliance/Compliance.Web/Controllers/ComplaintsController.cs:172 | save :170 → publish → save :180 | No | Drop save :170 |
| 44 | Compliance | B | ILegalJudgmentIssued | src/Services/Compliance/Compliance.Web/Controllers/ComplianceExtendedControllers.cs:118 (`LegalCasesController.Close`) | save :116 → publish → save :127 | No | Drop save :116 |
| 45 | Compliance | B | IDisciplinaryActionTaken | ComplianceExtendedControllers.cs:207 (`DisciplinaryActionsController.Create`) | save :196 → HTTP `fileRefs.RegisterReferenceAsync` :201 → publish :207 → save :220 (an HTTP call widens the gap) | No (Id set client-side :193) | Drop save :196 (move the second file-ref registration before the single save or after it, idempotent) |
| 46 | Compliance | B | IExportJobRequested | src/Services/Compliance/Compliance.Infrastructure/ExportJobs/ComplianceExportJobService.cs:75 | save :73 → publish → save :83. Entries: Compliance.Web/Controllers/ExportController.cs:30, :49 | No | Drop save :73 |
| 47 | HRHealth | B | IMedicalDecisionIssued | src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs:85 (`MedicalCommitteeReportsController.Create`) | save :81 → (if ShouldPublishDecision) publish → save :94 | No | Drop save :81; save once after the conditional publish |
| 48 | HRHealth | B | IMedicalRecommendationIssued | src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs:66 | save :62 → audit log → publish → save :77 | No | Drop save :62 |
| 49 | HRTraining | OK | ITrainingCompleted (e-learning) | src/Services/HRTraining/HR.Training.Web/Controllers/EssLearningController.cs:121 (`UpdateProgress`) | change + certificate add → publish → one save :139 | No (certificate number computed client-side :114) | — |
| 50 | HRTraining | B | ITrainingCompleted | src/Services/HRTraining/HR.Training.Web/Controllers/TrainingEnrollmentsController.cs:176 (`Complete`) | save :174 → publish → save :191 | No | Drop save :174 |
| 51 | HRTraining | B | IMissionApproved | src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs:408 (`MissionFilesController.Approve`) | save :406 → publish → save :418 | No | Drop save :406 |
| 52 | IDCard | B | ICardIssued | src/Services/IDCard/IDCard.Web/Controllers/IssuedCardsController.cs:94 (`Create`) | serial via code-gen HTTP :73 → add → save :92 → publish → save :102 | No (serial obtained before the save) | Drop save :92 |
| 53 | IDCard | B | ICardIssued (renewal) | IssuedCardsController.cs:321 (`Renew`) | revoke old + add replacement → save :319 → publish → save :329 | No | Drop save :319 |
| 54 | RecordsArchive | NO-OUTBOX (after save) | IExportJobRequested | src/Services/RecordsArchive/Records.Archive.Infrastructure/ExportJobs/ArchiveExportJobService.cs:60 | save :59 → publish (broker) :60 → save :67 (no-op). Crash between → job stays Pending, no event | No | Add outbox to RecordsArchive, or a sweeper re-publishing stale Pending jobs |
| 55 | RecordsArchive | C (no outbox) | ITransactionArchived | src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs:27 (`LeaveApprovedConsumer`) | archive ref via code-gen HTTP :20 → save :25 → context.Publish :27 → inbox commit. Rollback after publish → HRContracts archives a transaction with an ArchiveRef that was never stored; retry mints a new ref | No | Outbox in RecordsArchive (consumer outbox) |
| 56 | RecordsArchive | C (no outbox) | ITransactionArchived | LeaveAndAdvanceApprovedConsumer.cs:57 (`AdvanceApprovedConsumer`) | save :55 → publish :57 | No | as #55 |
| 57 | DevAnalytics | NO-OUTBOX (after save) | IExportJobRequested | src/Services/DevAnalytics/Dev.Analytics.Infrastructure/ExportJobs/AnalyticsExportJobService.cs:63 | save :62 → publish (broker) :63 → save :70 (no-op). Entry: Dev.Analytics.Web/Controllers/ExportJobsController.cs:17 | No | as #54 |
| 58 | Shared | C (base) | ISeparationPartyCompleted / ISeparationPartyFaulted | src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs:30 / :42 (`SeparationPartyAckConsumerBase`) | subclass Execute/Compensate (saves) → context.Publish; Faulted then rethrow. Concrete rows #62-#75 | No | Consumer outbox per service |
| 59 | Shared | C (base) | IHirePartyCompleted / IHirePartyFaulted | PartyAckConsumerBase.cs:79 / :91 (`HirePartyAckConsumerBase`) | as #58 | No | as #58 |
| 60 | Shared | C (base) | ITransferPartyCompleted / ITransferPartyFaulted | PartyAckConsumerBase.cs:126 / :138 (`TransferPartyAckConsumerBase`) | as #58 | No | as #58 |
| 61 | Shared | C (base) | ISecondmentPartyCompleted / ISecondmentPartyFaulted | PartyAckConsumerBase.cs:175 / :187 (`SecondmentPartyAckConsumerBase`) | as #58 | No | as #58 |
| 62 | RecordsArchive | C (no outbox) | ISeparationParty* | Records.Archive.Infrastructure/Consumers/PartyAckConsumers.cs:23 `SeparationPartyAckConsumer` → base :30/:42 | Execute save :94 / Compensate save :136 → publish | No | Outbox needed first |
| 63 | RecordsArchive | C (no outbox) | IHireParty* | PartyAckConsumers.cs:152 `HirePartyAckConsumer` → base :79/:91 | saves :186 / :226 → publish | No | Outbox needed first |
| 64 | ITInfrastructure | C (no outbox) | ISeparationParty* | src/Services/ITInfrastructure/IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs:9 `SeparationPartyAckConsumer` | saves :29 / :44 → publish | No | Outbox needed first |
| 65 | ITInfrastructure | C (no outbox) | IHireParty* | IT.Infra PartyAckConsumers.cs:52 `HirePartyAckConsumer` (no override) | publish only | No | — / accept |
| 66 | IDCard | C | ISeparationParty* | src/Services/IDCard/IDCard.Infrastructure/Consumers/PartyAckConsumers.cs:11 `SeparationPartyAckConsumer` | saves :29 / :51 → publish | No | Consumer outbox |
| 67 | IDCard | C | IHireParty* | IDCard PartyAckConsumers.cs:57 `HirePartyAckConsumer` (no override) | publish only | No | Consumer outbox |
| 68 | HRHealth | C | ISeparationParty* | src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs:25 `SeparationPartyAckConsumer` | saves :69 / :117 → publish | No | Consumer outbox |
| 69 | HRHealth | C | IHireParty* | HR.Health PartyAckConsumers.cs:133 `HirePartyAckConsumer` | saves :167 / :201 → publish | No | Consumer outbox |
| 70 | AuthDirectory | C (no outbox) | ISeparationParty* | src/Services/AuthDirectory/Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs:10 `SeparationPartyAckConsumer` | saves :20 / :32 → publish | No | Outbox needed first |
| 71 | AuthDirectory | C (no outbox) | ITransferParty* | Auth PartyAckConsumers.cs:37 `TransferPartyAckConsumer` (no override) | publish only | No | — |
| 72 | AuthDirectory | C (no outbox) | IHireParty* | Auth PartyAckConsumers.cs:40 `HirePartyAckConsumer` | Execute: read-only check, throws InvalidOperationException if no user (→ Faulted, no retry); Compensate save :62 → publish | No | Outbox needed first |
| 73 | AuthDirectory | C (no outbox) | ISecondmentParty* | Auth PartyAckConsumers.cs:66 `SecondmentPartyAckConsumer` (no override) | publish only | No | — |
| 74 | AdminLogistics | C (no outbox) | ISeparationParty* | src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs:10 `SeparationPartyAckConsumer` | saves :29 / :50 → publish | No | Outbox needed first |
| 75 | AdminLogistics | C (no outbox) | ITransferParty* | Admin PartyAckConsumers.cs:56 `TransferPartyAckConsumer` (no override) | publish only | No | — |
| 76 | Shared | — (dead) | any (replay) | src/Shared/HRManagement.ServiceDefaults/ErrorQueueReplay.cs:37 (`ReplayAsync`) | No production caller (tests use `EnsureTenantMatch` only; AcInt37 replays via the broker directly) | — | None; note if ever called inside an outbox scope it needs a following SaveChanges |

Counts: A 0 · B 23 (#1-5, 8-12, 20, 37-38, 43-48, 50-53; 3 of them ExecuteUpdate-based: #5, #8, #9) · OK 8 (#13-19, 49) ·
NO-OUTBOX 2 (#54, #57) · C 39 (#6-7, 24-36, 39-42, 55-56, 58-75 incl. 4 base-class rows and 2 scheduled sends) · dead 4 (#21-23, 76).

### Consumers / sagas that publish from ConsumeContext

| Service | File | Class | Publishes |
|---------|------|-------|-----------|
| HRContracts | src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/HireCaseStateMachine.cs | HireCaseStateMachine (saga) | IExecuteHireParty, IHireCaseCompleted, IProcessInterventionRequested, HirePartyTimeoutMsg (schedule) |
| HRContracts | src/Services/HRContracts/HR.Contracts.Infrastructure/Sagas/SeparationStateMachine.cs | SeparationStateMachine (saga) | IExecuteSeparationParty, ISeparationCompleted, ISeparationSagaFinalized, IProcessInterventionRequested, SeparationPartyTimeoutMsg (schedule) |
| HRContracts | src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/SagaSupportConsumers.cs | ContractsSeparationPartyConsumer | ISeparationPartyCompleted |
| HRContracts | src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs | CandidateAcceptedConsumer, EmployeeOfferAcceptedConsumer (indirect: MediatR → IEventPublisher → consume-scope IPublishEndpoint) | IProcessInterventionRequested |
| Payroll | src/Services/Payroll/Payroll.Infrastructure/Consumers/PayrollEventConsumers.cs | SeparationPartyConsumer, TransferPartyConsumer, HirePartyConsumer, SecondmentPartyConsumer | I{Separation,Transfer,Hire,Secondment}PartyCompleted / Faulted |
| RecordsArchive | src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/LeaveAndAdvanceApprovedConsumer.cs | LeaveApprovedConsumer, AdvanceApprovedConsumer | ITransactionArchived |
| Shared | src/Shared/HRManagement.ServiceDefaults/PartyAckConsumerBase.cs | SeparationPartyAckConsumerBase, HirePartyAckConsumerBase, TransferPartyAckConsumerBase, SecondmentPartyAckConsumerBase | party Completed / Faulted |

PartyAckConsumerBase subclasses:

| Service | File | Concrete class : base |
|---------|------|-----------------------|
| RecordsArchive | Records.Archive.Infrastructure/Consumers/PartyAckConsumers.cs:23, :152 | SeparationPartyAckConsumer : Separation…Base; HirePartyAckConsumer : Hire…Base |
| ITInfrastructure | IT.Infra.Infrastructure/Consumers/PartyAckConsumers.cs:9, :52 | SeparationPartyAckConsumer; HirePartyAckConsumer |
| IDCard | IDCard.Infrastructure/Consumers/PartyAckConsumers.cs:11, :57 | SeparationPartyAckConsumer; HirePartyAckConsumer |
| HRHealth | HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs:25, :133 | SeparationPartyAckConsumer; HirePartyAckConsumer |
| AuthDirectory | Auth.Directory.Infrastructure/Consumers/PartyAckConsumers.cs:10, :37, :40, :66 | SeparationPartyAckConsumer; TransferPartyAckConsumer; HirePartyAckConsumer; SecondmentPartyAckConsumer |
| AdminLogistics | Admin.Logistics.Infrastructure/Consumers/PartyAckConsumers.cs:10, :56 | SeparationPartyAckConsumer; TransferPartyAckConsumer |
| HRMovements (other inventory) | HR.Movements.Infrastructure/Consumers/SagaSupportConsumers.cs:91, :215 | Transfer…Base; Secondment…Base subclasses |

Behaviour worth flagging for every party consumer (base classes and Payroll): the `*PartyFaulted` is published in the
`catch` and the exception is rethrown. The inbox transaction rolls back, and for transient exceptions the retry policy
(`MassTransitExtensions.cs:145-171`, 3 retries) runs the consumer again, so one failure can emit up to four Faulted
messages, and a later successful attempt emits Completed after the saga has already moved to AwaitingIntervention, where
`PartyCompleted` is ignored (`HireCaseStateMachine.cs:160`, SeparationStateMachine.cs:183).
That saga would stay in intervention even though the party succeeded.

### Missing from or wrong in docs/EVENT_PUBLISHER_INVENTORY.md (scope of this group)

Wrong rows:
- `IHireCaseCompleted` listed under `PublishHireCaseHandler` ("ExecuteUpdate → Publish → Save"): that handler publishes the command `IStartHireSaga`. `IHireCaseCompleted` comes from `HireCaseStateMachine.PublishFinal` (saga, C). The wrapper `PublishHireCaseCompletedAsync` has no caller.
- `ISeparationCompleted` listed under `PublishSeparationHandler`: that handler publishes `IStartSeparationSaga`. `ISeparationCompleted` comes from `SeparationStateMachine.PublishFinalCompleted` (saga, C). The wrapper `PublishSeparationCompletedAsync` has no caller.
- Every "Save → Publish → Save" row is labelled Transactional, but these are class B (not atomic): IPayrollProcessed, ILeaveApproved (really ExecuteUpdate ×2 → Save → Publish → Save), IAdvanceApproved, IConfirmationApproved, IJobUpdateApplied, ISeparationStepCompleted, IContractActivated and ICommencementRecorded (both also save inside the HireCaseSupport helpers), IMedicalRecommendationIssued, IMedicalDecisionIssued, ILegalJudgmentIssued, IDisciplinaryActionTaken, IComplaintResolved, ICardIssued, ITrainingCompleted (enrolment), IMissionApproved.

Missing rows:
- `ILeaveCancelled`: CancelLeaveRequestHandler (B, ExecuteUpdate-based).
- `IStartSeparationSaga`: PublishSeparationHandler. `IStartHireSaga`: PublishHireCaseHandler.
- `ICancelSeparationSaga`: CancelSeparationHandler (and DeleteSeparationRecordHandler through it), ProcessInterventionsController.Cancel.
- `IResumeSeparationSaga`, `IResumeHireSaga`, `ICancelHireSaga`: ProcessInterventionsController.
- `IProcessInterventionRequested`: ProcessCandidateAcceptedHandler (consumer path), HireCommencementWatcher (hosted service), both sagas.
- `IExportJobRequested`: ContractsExportJobService, PayrollExportJobService, ComplianceExportJobService (B), ArchiveExportJobService and AnalyticsExportJobService (no outbox).
- `ICardIssued` renewal path (`IssuedCardsController.Renew`).
- `ITransactionArchived`: RecordsArchive LeaveApprovedConsumer / AdvanceApprovedConsumer (no outbox; the HRContracts wrapper method is dead).
- All saga-published messages: IExecuteHireParty, IExecuteSeparationParty, ISeparationSagaFinalized, saga timeout schedules.
- All party ACKs: I{Separation,Hire,Transfer,Secondment}Party{Completed,Faulted} from PartyAckConsumerBase (14 concrete subclasses in this group), Payroll's four party consumers and ContractsSeparationPartyConsumer.
- The doc has no class for consumer/saga publishes at all, and does not mention the dead wrappers (`PublishTransactionArchivedAsync`, `PublishSeparationCompletedAsync`, `PublishHireCaseCompletedAsync`) or `ErrorQueueReplay.ReplayAsync` (no production caller).
