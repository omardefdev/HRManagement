# PLAN-60 — Baseline (P0, «المرحلة 0»)

Taken on 2026-11-27/28 (UTC) on the owner's Linux notebook, branch `feature/plan-60-sep-unblock-decision-governance` from
`origin/main` `96c9594d` (PLAN-59 merged through PR #60). Generated inventories are under `artifacts/plan60/inventory.*` (not
committed); this file summarises them.

## 0.1 — PLAN-46's contract, by name

Every piece is present under the name the plan expects; **no adapter is needed**.

| Piece | Where |
|-------|-------|
| `IWorkflowApprovalClient` (`StartAsync`, `CancelAsync`, `GetStatusAsync`) | `src/Shared/HRManagement.ServiceDefaults/Workflow/CrossService/IWorkflowApprovalClient.cs` |
| `WorkflowApprovalStartRequest` (with `SubjectEmployeeId`) | `…/CrossService/WorkflowApprovalStartRequest.cs` |
| `WorkflowApprovalStartResult`, `WorkflowApprovalCancelResult`, `WorkflowApprovalStatusDto`, `WorkflowApprovalOutcomes`, `WorkflowOutcomeDecision` | `…/CrossService/WorkflowApprovalResults.cs` |
| `WorkflowApprovalErrorCodes` (with `MessageFor`; `DefinitionMissing`, `PolicyDenied`, `EngineUnavailable`, `Forbidden`, `Conflict`, `Invalid`) | `…/CrossService/WorkflowApprovalErrorCodes.cs` |
| `WorkflowOutcomeGuard` (`ShouldApply`, `HasExcludedDecider`, `TouchMetric`) | `…/CrossService/WorkflowOutcomeGuard.cs` |
| `WorkflowApprovalSubmission` (`SubmitAsync`, `WithdrawAsync`, `UseTenant`) and `WorkflowApprovalSubmitSteps` | `…/CrossService/WorkflowApprovalSubmission.cs` |
| `IWorkflowApprovalOwnerStore`, `WorkflowApprovalSweepJob<TStore>`, `WorkflowApprovalSweepOptions` (`IntervalMinutes` 10, `StaleAfterMinutes` 15) | `…/CrossService/WorkflowApprovalSweepJob.cs` |
| `AddWorkflowApprovalClient`, `AddWorkflowApprovalSweep<TStore>` | `…/CrossService/WorkflowApprovalClientExtensions.cs` |
| `IWorkflowOutcomeDecided` | `src/Shared/HRManagement.MessageContracts/Events/IWorkflowOutcomeDecided.cs` |
| `FakeWorkflowApprovalClient` | `src/Tests/HRManagement.TestSupport/Workflow/FakeWorkflowApprovalClient.cs` |
| Registry entry `SeparationRecord` → `SeparationApprovalOutcomeConsumer`, `PendingPlan: "PLAN-60"` | `src/Tests/HRManagement.UnitTests/Architecture/WorkflowDefinitionsHaveHandlersTests.cs:27` |
| HR.Core: `WorkflowInstanceActionService.HandledEntityTypes` includes every `Generic` external type (so `SeparationRecord`) | `HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs:74` |
| `AdvisoryLockKeys`: card sweep `4_001_011`, last `PayProfileChangeActivation = 4_001_013` | `src/Shared/HRManagement.ServiceDefaults/AdvisoryLockKeys.cs` |

Two behaviours of the contract that this plan relies on (read from the code):
- `WorkflowApprovalSubmission.WithdrawAsync` answers `Success=false, Error=null, FinalOutcome=<status>` when the instance had
  ended: the owner's outcome has been applied, and the caller must read the record again rather than show an error.
- `WorkflowApprovalSubmission.SubmitAsync` calls `LinkAndSaveAsync(instanceId, number)` once per submission; the owner sets
  `PendingApproval`, the instance and the number in that one save.

## 0.3 — Build

`dotnet build HRManagement.slnx -warnaserror`: **0 errors, 0 warnings** (4 min 47 s).

## 0.4 — Tests (the plan's filters, widened)

| Run | Filter | Result |
|-----|--------|--------|
| Unit | `Fun002\|Separation\|DOM001\|S2s042\|Wf043\|Wf046\|WorkflowDefinitionsHaveHandlers\|WorkflowStageGuardRole\|Iam051\|WriteActionsHaveExplicitPolicy\|OrgScopeCoverage` | **640 passed**, 0 failed (34 s) |
| Integration (SQLite/in-memory) | `Fun002\|Int003\|G3Separation\|ContractSeparationChain\|ContractLifecycle\|EndToEndReleaseWorkflow\|SoDForgedApprover\|OutboxAtomicity\|IdempotentConsumer` | **46 passed**, 0 failed (43 s) |
| Integration, `RUN_TESTCONTAINERS=true` | the same, plus `Iam052ServiceScope` | **49 passed**, 0 failed (69 s) |
| Architecture | `WriteActionsHaveExplicitPolicy\|OrgScopeCoverage` (in `HRManagement.ArchitectureTests`) | **119 passed**, 0 failed |

Per class (all green): unit — `CrossTenantBoundedContextIsolationTests` 1, `Data002EmployeeLifecycleTests` 2, `Fun002ChecklistGovernanceTests` 4,
`Fun002JourneyMatrixTests` 1, `Iam051*` 50, `S2s042*` 249 (`S2s042LifecycleGateTests` 28), `SeparationCompletedConsumerTests` 1,
`SeparationOfDutiesTests` 22, `SeparationSagaCompensationTests` 1, `SeparationWorkflowRulesTests` 11, `Syr009RegimeLifecycleTests` 2,
`Wf043*` 122, `Wf046*` 143 (`Wf046RegistryEnforcedTests` 6, `Wf046HrCoreSideTests` 31), `WorkflowDefinitionsHaveHandlersTests` 10,
`WorkflowStageGuardRoleTests` 17, and a few others the filter caught (`HealthPartyCompensationTests`, `HrContractsEventContractsTests`,
`Pol049LeaveChannelsTests`, `RecordsArchivePartyCompensationTests`); integration — `ContractLifecycleIntegrationTests` 11,
`ContractSeparationChainIntegrationTests` 2, `EndToEndReleaseWorkflowSmokeIntegrationTests` 1, `Fun002SeparationRecordIntegrationTests` 7,
`Fun002SeparationSagaIntegrationTests` 2, `G3SeparationMidSagaFailureIntegrationTests` 2, `IdempotentConsumerTests` 3,
`Int003ProcessSagaTests` 5, `OutboxAtomicityIntegrationTests` 5, `SoDForgedApproverTests` 8, `Iam052ServiceScopeHttpTests` 3 (Testcontainers).

Red on `main` before this plan, not this plan's (recorded, not chased): `scripts/pol049-validate.ps1` **FAIL (28)**, exit 1;
`scripts/acceptance-baseline-validate.ps1` exit 1 (the committed `acceptance-evidence` summary's totals: «96 ACs: 0 proven … 69 stale,
4 incomplete, 23 not run»).

## 0.5 — The data

### The seed (`DemoContractsSeeder.SeedSeparationsAsync`, deterministic)

- One record per employee whose status bucket is ≥ 95, `AcceptanceSeedExecution.SeparationCount` of them: **86** on the acceptance
  stack (`DATA-SEPARATION`), fewer in demo mode.
- `SeparationEventPublished = n % 3 != 0` → **57 published, 29 unpublished** (n = 0..85: 29 multiples of 3); none cancelled; no
  `Guid.Empty`; no saga row; no duplicate (one record per employee by construction); `ApprovedBy = EmployeeId(0)`.
- Every `SeparationChecklistDefaults.DefaultSteps` item per record (six today), the first two always completed, the rest when n is even.
- So the backfill's rules give the seed **57 `Executing`** (published, no saga row) and **29 `Draft` (marked legacy)**. On a fresh stack
  the migration meets empty tables and the seeder writes these statuses itself (P-13); the first local stack shows them (P6).

### The demo (`artifacts/plan60/vps/picture-p0.txt`, read only, 2026-11-27T23:4xZ)

See «The demo's picture» below: 26 records, 16 published and 10 not; no saga row; nothing for rules 4 and 5 to cancel.

## Inventories (generated by search)

### The writers (`artifacts/plan60/inventory.code.txt`)

- **`SeparationEventPublished` set:** `PublishSeparationHandler` (`SeparationRecordHandlers.cs:205`, true), `SeparationSagaFinalizedConsumer`
  (`SagaSupportConsumers.cs:73`, true), `ContractsSeparationPartyConsumer` on compensate (`SagaSupportConsumers.cs:136`, false), the
  creators (false: `SeparationRecordHandlers.cs:85`, `InboundEventCommands.cs:468`), `DemoContractsSeeder.cs:512`.
- **`IsCancelled` set:** `SeparationWorkflowRules.SoftCancel` (`:51`) only (called by `CancelSeparationHandler`, and through it `DeleteSeparationRecordHandler`).
- **`ApprovedBy` set:** `SeparationWizardController.Step1` (`:85`, the creator), `CreateSeparationRecordHandler` (`:83`, from the DTO that
  the controller fills with the creator), `UpdateSeparationRecordHandler` (`:117`, kept), `DemoContractsSeeder` (`EmployeeId(0)`).
- **`StartSeparationSagaAsync`:** `PublishSeparationHandler` (`:195`) only (plus the test spies). **`CancelSeparationSagaAsync`:**
  `CancelSeparationHandler` (`:330`) only.
- **A `Separation` transaction moved:** `PublishSeparationHandler` (`:214`, the employee's latest, `Draft`→…→`Executing`, actor null),
  `CancelSeparationHandler` (`:339`, the employee's latest, → `Cancelled`, actor null), `SeparationSagaFinalizedConsumer` (`:79`, the
  employee's latest, `Executing`→`PostProcessing`→`Completed`→`Archived`, actor null), `ServiceTransactionsController.Advance` (any).
- **Readers of `SeparationEventPublished`** (rule 3 keeps the column): `SeparationWorkflowRules.CanComplete`/`CanCancel`, the handlers'
  guards (`:109`, `:147`, `:244`, `:292`, `:325`), `ProcessSeparationRequestHandler`'s idempotency (`InboundEventCommands.cs:451`), the
  controller's list filter and Edit view model, the views (`Index`, `Edit`, `Details`, `Step3`), `ServiceContractsDtos.cs:64`, and
  `AcS2s42TrustBoundaryTests.cs:687` (psql read).

### The entries (every creation path)

`SeparationWizardController.Step1` (builds the entity itself), `CreateSeparationRecordHandler` (`SeparationRecordsController.Create`),
`ProcessSeparationRequestHandler` (fed by `LegalJudgmentIssuedConsumer`, `MedicalDecisionIssuedConsumer`, `ResignationSubmittedConsumer`),
and `DemoContractsSeeder`. None checks the employee's status, the contract's owner, or another open record.

### The POSTs

| Controller | Action | Policy | Stance | Antiforgery |
|------------|--------|--------|--------|-------------|
| `SeparationWizardController` | `Step1` | `HrContractsWrite` | class `[OrgScoped(SeparationRecord, Forbid)]` | yes |
| | `Step2CompleteStep(id, step, note)` | `HrContractsWrite` | class | yes |
| | `Step2Confirm` (writes nothing) | `HrContractsWrite` | class | yes |
| | `Step3Execute` (sends `PublishSeparationCommand`) | `HrContractsWrite` | class | yes |
| `SeparationRecordsController` | `Create`, `Edit`, `Delete` | `HrContractsWrite` | class `[OrgScoped(SeparationRecord, Forbid)]` | yes |
| | `PublishSeparation`, `Cancel` (no reason), `OverrideChecklistStep(id, step, reason, Guid approvedBy)` | `HrContractsApprove` | class | yes |
| `AdministrativeDecisionsController` | `Create`, `Edit`, `Delete` | `HrContractsWrite` | class `[OrgScoped(AdministrativeDecision)]` | yes |
| `ProcessInterventionsController` (`api/process-interventions`, `[ApiController]`) | `Resume`, `Cancel` | `HrContractsWrite` | `[OrgScopeCentralOnly]` | **no** |
| `ServiceTransactionsController` | `Advance(id, nextState, reason)` | `HrContractsApprove` | class `[OrgScoped(ServiceTransaction, Forbid)]` | yes |

### HR.Core's side

- `ExternalApprovalTypes`: `SeparationRecord` owned by `svc-hr-contracts`, codes `SEPARATION-APPROVAL` and `SEPARATION-APPROVAL-SENSITIVE`;
  `WorkflowGuardrails` for both; `SEPARATION-WIZARD` in `WorkflowPackSeeder.RetiredCodes` (seeded `Retired`, unpublished).
- `WorkflowPackSeeder.Workflows` ends at `f0050017-0017-4000-8000-000000000017` (`RECRUITMENT-RESULTS-APPROVAL`); `DistinctApproverCodes`
  = `ASSET-ASSIGN-APPROVAL`, `ASSET-LOSS-APPROVAL`, `DISCIPLINARY-APPROVAL`; `EnsureCrossServiceChainsAsync` raises the card and the discipline.
- `WorkflowDefinitionsHaveHandlersTests` requires the consumer type in HR.Contracts' infrastructure assembly once `PendingPlan` goes.

### The tests (`artifacts/plan60/inventory.tests.md`, generated by a read-only search, 181 lines)

The classes P-13 adapts, each with what breaks under the state machine:

- **Integration, rewritten onto the new path:** `ContractLifecycleIntegrationTests` (7 separation facts: create, publish, complete,
  override, cancel, update), `Fun002SeparationRecordIntegrationTests` (7), `EndToEndReleaseWorkflowSmokeIntegrationTests`,
  `Int003ProcessSagaTests.DOM001_Publish_DoesNotAutoComplete_ContractTerminated` (its premise flips: the execution now completes the
  contract's end), `G3SeparationMidSagaFailureIntegrationTests`, `ContractSeparationChainIntegrationTests`,
  `Fun002SeparationSagaIntegrationTests`, `OutboxAtomicityIntegrationTests` (the spy publisher), `SoDForgedApproverTests` (gains the new
  actions), `Iam052ServiceScopeHttpTests` (plants a record), `Plan034ScopedIndexTests` (**plants 110 open records for one employee**: the
  partial unique index refuses them, so all but one are cancelled through the state machine, the page-size meaning kept).
- **Unit:** `SeparationWorkflowRulesTests` (11, the old rules), `Phase7_BusinessLogicDepthTests` (`:332`), `Fun002ChecklistGovernanceTests`,
  `Fun002JourneyMatrixTests`, `S2s042LifecycleGateTests` (the start check now needs `status`; its list of files that write
  `new SeparationRecord`), `SeparationSagaCompensationTests`, `CrossTenantBoundedContextIsolationTests`, `WorkflowDefinitionsHaveHandlersTests`
  (`PendingPlan` goes; **`R-SEP-START` stops being an orphan trigger** once `SeparationRecord` is handled), `Wf046RegistryEnforcedTests`
  (the skip goes), `Wf043DefinitionLifecycleTests:336` and `Wf043GovernanceIntegrationTests:113` (the distinct-approver list gains the two codes).
- **Test hosts:** `AddContractsApplicationForTests` (`ContractsTestServices.cs:30`) builds with `ValidateOnBuild` and uses
  `PublishSeparationHandler` as its marker: every new dependency is registered there.
- **AC-QA-34e** (`AcQa34PushableBaselineTests`) counts eleven integration tests by exact name; the names are kept.
- **Acceptance:** AC-INT-37a (no status or contract filter), AC-INT-37e (prefers `EmpBr1Id`, one new record: a rerun meets the open record),
  AC-S2S-42h (create, psql checklist, `PublishSeparation`); **server:** `AcWf43VpsChecks` (`Published|t|12` → 14, the distinct-approver list).
- **Counts:** 86 seeded separations in `AcceptanceV2CatalogTests:18`, `scripts/acceptance-verify.ps1:534`, `testdata/acceptance/manifest.json:12`;
  no test asserts the six step values; no script counts workflow definitions; `DemoPayrollSeeder.cs:569` reuses the seeded separation ids
  (kept stable).
- **Guards to keep in mind:** `Wf044DefinitionSeedingArchitectureTests` (only `WorkflowPackSeeder.cs` creates definitions),
  `Pol049RulesInventoryTests` (a changed `*Rules.cs` needs `scripts/policy-inventory.ps1` rerun).
- **Gap found:** no test drives HR.Contracts' three inbound separation consumers (judgment, medical decision, resignation); P1 adds one
  through the factory.

## The demo's picture (P-17), read only

`ssh hrm-vps 'bash -s' < artifacts/plan60/vps/picture.sh` → `artifacts/plan60/vps/picture-p0.txt` (12 s, one connection; never a name or an amount):

- **Tag `8086ac8629af`** (16 application images), 21 containers up and healthy, **46 G free** of 145 G.
- hr_contracts_db's last migration `20261104012037_MSG040_InboxStateKeys`.
- **`SeparationRecords`: 26** in one tenant — `IsCancelled=f, SeparationEventPublished=f`: **10**; `f, t`: **16**; cancelled: 0.
  `EmployeeId = Guid.Empty`: **0**. `SeparationSagaStates`: **0 rows**.
- **Predicted backfill:** rule 1 → 0 `Cancelled`; rule 2 → **16 `Executing`** (published, no saga row); rule 3 → **10 `Draft`** (marked
  legacy); rule 4 → 0; rule 5 → **0 open duplicates, 0 records to cancel, 0 employees with two started records** (no Stop).
- Checklist rows: 156 = 26 × 6 (ContractTerminated and PayrollSettled 25 completed of 26; the other four 13 of 26).
- `Separation` transactions: **9** (Draft 2, Submitted 1, UnderReview 1, InProcess 1, PendingApproval 1, Approved 1, Completed 1, Archived 1);
  records with a `Separation` transaction of the same employee created within 5 minutes: **1** (the upper bound of rule 7's links).
- `AdministrativeDecisions`: 50 (all `DEC-2026-…`, no `SEP`). Contracts: Active 470, Expired 15, Terminated 15.
- HR.Core `WorkflowDefinitions`: **Published 12, Retired 9**; `SEPARATION-WIZARD` v1 Retired; distinct approvers on
  `ASSET-ASSIGN-APPROVAL`, `ASSET-LOSS-APPROVAL`, `DISCIPLINARY-APPROVAL`; no `SeparationRecord` instance. Employees by status (stored as
  numbers): 0: 425, 2: 15, 3: 10, 4: 25, 5: 25.
- Checksums: `SeparationRecords` `26|83debd34c0f24bea266cbbe53b3a3ec6`; `Contracts` `500|be2211274ac370bba99afdfb4437a1a0`;
  Payroll runs by status 1:1, 2:4, 4:1, 8:15 (all Regular), runs `21|8da67ec21ecc0841c5a610d29bf346a2`, lines
  `3400|b242a82557082234d600589c1049cf96`, batches `6|31924d4517c7a7d8112ac29885438542`, orders `0|`, arrears
  `15|747ae32f3066ecc513aa302862d79c82`, pay profiles `425|61c32073ecfc0bbe7d242e46a87b8f0b`.
