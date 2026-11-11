# WF-001: Configurable Workflow Engine

## Overview
National-grade configurable workflow engine supporting sequential multi-step approvals with conditions, version pinning, delegation, and domain guardrails. Enables agency-specific approval chains without code forks.

## Architecture

```
SharedKernel/Workflow/          → Enums + DTOs (cross-service)
  ├── WorkflowStatus.cs
  ├── WorkflowStepStatus.cs
  ├── WorkflowStepType.cs
  ├── WorkflowDefinitionSnapshot.cs
  ├── WorkflowStepSnapshot.cs
  ├── WorkflowStepHistoryEntry.cs
  └── WorkflowCondition.cs

HR.Core.Domain/Entities/Workflow/ → EF Entities
  ├── WorkflowDefinition.cs
  └── WorkflowInstance.cs          (PLAN-50 removed the unused WorkflowDelegation.cs)

ServiceDefaults/Workflow/       → Engine Implementation
  ├── IWorkflowEngine.cs
  ├── WorkflowEngine.cs
  └── WorkflowEngineExtensions.cs

HR.Core.Web/Controllers/        → API Layer
  ├── WorkflowDefinitionsController.cs
  └── WorkflowInstancesController.cs
```

## Key Concepts

### WorkflowDefinition
- `Code` + `Version` uniquely identify a definition (Code is reusable across versions)
- `DomainEntityType` links to the business entity it governs (e.g., "LeaveRequest", "Transfer")
- `StepsJson` contains `WorkflowStepSnapshot[]` — ordered sequential steps
- `GuardrailRolesJson` — domain invariant roles that prevent misconfiguration
- `IsPublished` — only published definitions can be used to start instances
- `ValidFrom`/`ValidTo` — effective date window

### WorkflowInstance
- Created when a workflow is started against a published definition
- `DefinitionVersion` — pinned at creation time (never changes)
- `CurrentStepLevel` — tracks progress through the steps
- `Status` — InProgress → Completed/Rejected/Cancelled
- `StepHistoryJson` — audit trail of all actions taken
- `ApprovalRequestId` — optional link to legacy `ApprovalRequest` for bridge mode

### WorkflowCondition
Simple field-based evaluation: `{ Field, Operator, Value }`.
Operators: Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, Contains, In.
Evaluated against a JSON context dictionary attached to the instance.

### Delegation (PLAN-50)
A delegation is an `ApprovalDelegation` (the only delegation entity; rules in `docs/SOD_MATRIX.md`). It has a real effect in the
engine:
- `WorkflowApprovalEvaluator.GetDecisionAsync` keeps its order — structure, step role (direct, then delegated), `WorkflowSodRules`,
  the entity gate, the initiator rule. When the actor lacks the step's role, a delegation in force of that role covering the
  instance's entity type stands in for it (`ApprovalDelegationMatcher`); the evaluator reads the actor's loaded delegations or asks
  `IApprovalDelegationLookup` (HR.Core: `DbApprovalDelegationLookup`, no cache). Every identity rule after the role then runs twice,
  as the delegate and as the principal (`WorkflowActionActor.PrincipalOf`, with the principal's stored branch scope); any refusal wins.
  The direct role wins (`OnBehalfOf` stays null). A failing lookup means no delegation. Exempt types are untouched.
- The route policy `WorkflowStepAction` is roles or a delegate of any delegable role (still no service account); it replaces
  `HrCore.BranchOperation` on the MVC workflow actions of requisitions, offers, committees, establishment changes and competitions.
- An action on behalf writes `OnBehalfOfId`, `OnBehalfOfName` and `DelegationId` into its `WorkflowStepHistoryEntry` (optional
  fields, never written by a direct action, so older history reads unchanged); its signature adds `onBehalfOfUserId` and
  `delegationId` to the payload and is signed with the delegated role. A direct action signs the twelve fields it always signed.
- The distinct-approver rules count an approval on behalf for the delegate and for the principal.

### Server-side step role and entity gates (PLAN-25, G-04)
Every approve **and** reject through `WorkflowInstanceActionService` (HR.Core) goes through `WorkflowApprovalEvaluator`
(`HR.Core.Infrastructure/Services/Workflow/Gates/`), in this order:

1. `WorkflowStageGuard.ValidateStructure` — instance in progress, no stage skipping, the current step exists.
2. `WorkflowStageGuard.ValidateStepRole` — the step's `RoleRequired` must be one of **all** the actor's roles
   (`WorkflowActionActor.FromPrincipal`: every `ClaimTypes.Role` / `role` claim, `employee_id`, `scope_org_unit_id`),
   case-insensitive; message «هذه الخطوة تتطلب دور «{role}».».
3. The entity gate (`IWorkflowApprovalGate` for the instance's entity type: requisition, offer, committee, appeal,
   competition, results, establishment change) — SoD, quorum, membership, assigned reviewer, distinct approvers, org scope.
4. `WorkflowStageGuard.ValidateInitiator` — the initiator never approves later stages (approve only).

Configuration (`Workflow` section of HR.Core.Web):

| Key | Default | Meaning |
|-----|---------|---------|
| `Workflow:AdminBypass` | `false` (all environments) | `hr-admin` may act on a step without its role only when true |
| `Workflow:EnforceStepRoleEntityTypes` | RecruitmentRequisition, OfferLetter, SelectionCommitteeSession, RecruitmentAppeal, RecruitmentCompetition, CompetitionResultVersion, EstablishmentChangeRequest | entity types whose step role is enforced; other domains (assets, transfers, …) keep their previous behaviour — follow-up |

The detail panels (`WorkflowPanelBuilder.BuildAsync`) ask the same evaluator, so the UI offers exactly what the server
accepts (`Hcm025PanelServerParityTests`); the work inbox uses the same role rule in its bulk check. Published definitions
must have an outcome handler or an allow-listed owner (`WorkflowDefinitionsHaveHandlersTests`); RECRUITMENT-HIRE is retired.

### Approval start (PLAN-25, G-02)
Recruitment and establishment approvals start through `ApprovalStartCoordinator`: the policy engine (when not Off) may deny
or start another workflow, otherwise the default workflow is started (`Recruitment:Approvals:AlwaysStartWorkflow`, default
true); the entity is `PendingApproval` only together with the instance. `NationalScope:IsLabScale` no longer decides it.

### Definition governance (PLAN-43, G-WF-04)
Only `workflow-admin` or `hr-admin`, from the national tenant, write definitions; no service account does. A version moves
`Draft` → `PendingPublish` → `Published` → `Retired` through `WorkflowDefinitionLifecycleService`:

- `submit` runs `WorkflowDefinitionValidator` (levels, names, roles in `HrManagementRoles.All`, a known entity type, the limits of
  `WorkflowGuardrails`, known condition operators) and returns every error in Arabic. A conditional step does not count towards
  the limits, and the last step has no condition. The validator runs again at `publish`.
- `publish` needs a person who is not the creator, the submitter or anyone who edited the draft.
- A published version is never edited: a change is a new version, and running instances stay on the version they pinned. A new
  version keeps the entity type of its code.
- `publish` warns when a `PolicyWorkflowBinding` pins another version.
- `retire` needs a reason and, for a type the engine starts, a newer valid published version: retiring the latest one would send
  every start back to an older version without a second person.
- Every start honours `ValidFrom` and `ValidTo`, through a binding too.
- Transitions race on `xmin`: the loser gets a 409.
- `WorkflowPackSeeder.EnsureVersionAsync` is the system path for a national definition (`ChangeReason = seed:<plan>`).
- `WorkflowDefinitionHealthReport` runs the validator on every published definition at start-up: a warning and
  `workflow_definition_invalid_total`, never a failed boot.

### External start (PLAN-43, G-WF-05)
`ExternalApprovalTypes` lists the types started from outside HR.Core and the one service account that owns each. Only
`ExternalApprovalService` starts them:

- Owner only, otherwise 403. The body is `WorkflowApprovalStartRequest`, which never names the requester.
- The requester comes from `X-Acting-User` (`X-Acting-User-Name` for display), accepted because the owning service vouches for it.
  A job without a request sets `ActingUserScope`. No header: 400.
- `IdempotencyKey` is looked up first, in every state: the same key returns the same instance even when finished. Another key for
  a running entity is 409.
- The latest valid published version of the code starts; a policy binding has no say. None, or all its steps skipped: 422.
- `POST {id}/cancel` withdraws (`Cancelled`, history `Withdrawn`), the owner only; a finished instance is 409.
- The policy engine only says an approval is needed: `PolicyWorkflowTriggerAllowlist` is empty and `PolicyOrchestrator` starts no
  registered type.
- Custody events are schema v2: `WorkflowInstanceId` comes from the instance. Their consumers apply an outcome only when it equals
  the id on the record, and ignore a v1 message, counting both in `workflow_outcome_ignored_total`.

### Separation of duties (PLAN-43, G-WF-06)
The step role is enforced for every type except `Workflow:StepRole:ExemptEntityTypes` (empty since PLAN-46)
(`docs/qa/workflow-step-role-exemptions.json`, each with an owner plan and a date). The old key is ignored with a start-up warning.

`WorkflowApprovalEvaluator` runs: service account refused → structure → role → `WorkflowSodRules` → entity gate → initiator rule.
`WorkflowPanelBuilder` calls the same rules:

- `Excluded`: the requester and `ExcludedApproverUserIds`, approving and rejecting.
- `Subject`: `SubjectEmployeeId`, approving and rejecting.
- `DistinctApprover`: approving only, when `RequireDistinctApprovers` is true (new definitions by default; `AssetAssignment` always,
  by its guardrail; not the seeded recruitment and establishment ones).

`Workflow:AdminBypass` never passes these rules.

### Instance integrity (PLAN-44, G-WF-12)

- **Concurrency.** `WorkflowInstance` carries `xmin`. A decision that loses the race gets `WorkflowConcurrency.ConflictMessage`, writes nothing and is not retried: the step may have moved on. The API answers 409. Forms and the API send the step they showed (`TargetStepLevel`), so a page behind the instance gets the same message instead of acting on the next step.
- **One running instance.** `IX_WorkflowInstances_Tenant_Entity_Active` is unique on `(TenantId, EntityType, EntityId)` where `Status = 'InProgress'`, and `IX_WorkflowInstances_Tenant_Source_IdempotencyKey` on `(TenantId, SourceClientId, IdempotencyKey)`. Every insert goes through `WorkflowInstanceInsert`, which takes a savepoint inside a transaction, because a violation would abort it. The coordinator returns the winner's instance when it may adopt it, otherwise «يوجد مسار جارٍ لهذا الطلب». An external start answers as if it had come second: the same key gets the same instance, another key on a running entity gets 409.
- **One transaction.** `ApproveAsync` and `RejectAsync` do every check first, then write inside `OutboxUnitOfWork`: the instance, the signature, the handler's change and the outbox row. A refusal stages nothing, so what a caller staged is not saved. A handler that throws rolls everything back. `WorkflowOutcomeRefusedException` does the same and shows its message: a change that cannot apply refuses the last approval, because nothing re-applies it later.
- **Reconciliation.** `WorkflowOutcomeReconciliationJob` runs every `Workflow:Reconciliation:IntervalMinutes` (15) for every tenant, each in its own scope with the tenant set before the connection opens. It logs a warning per item and updates `workflow_outcome_unapplied_total{entity_type}`. With `AutoApply=false` (the default) it repairs nothing. `ApprovalRepairService` has a second category, «finished instance, linked entity still pending»: the handler runs again and the operator is audited as the actor (`WorkflowOutcomeReapplied`). It creates and changes no instance. A completed instance the entity does not link is listed «غير مرتبطة» and never applied. External types are repaired by their owners (PLAN-45, PLAN-46).
- **Metrics.** `workflow_instance_conflict_total{action}` counts lost races.
- **Seeding.** Only `WorkflowPackSeeder` seeds definitions (`Wf044DefinitionSeedingArchitectureTests`).

### الاعتماد العابر للخدمات — cross-service approval (PLAN-46, G-WF-01)

A service that owns a decision (IDCard for a card request, Compliance for a disciplinary action, HR.Contracts for a separation
with PLAN-60) gets it approved in this engine through one contract, with the names PLAN-60 §0.1 relies on.

**The types** (`src/Shared/HRManagement.ServiceDefaults/Workflow/CrossService/`, `src/Shared/HRManagement.MessageContracts/Events/`):

| Type | What it is |
|------|------------|
| `IWorkflowApprovalClient` | `StartAsync`, `CancelAsync`, `GetStatusAsync`. Registered by `AddWorkflowApprovalClient` (named client `hr-core-workflow`, `Integration:HrCoreBaseUrl`, service token, acting user, tenant). |
| `WorkflowApprovalStartRequest` | The wire body of PLAN-43; `SubjectEmployeeId` stays optional and last. |
| `WorkflowApprovalStartResult(Success, InstanceId, AlreadyActive, ErrorCode, Error)` | `AlreadyActive` is HR.Core's: the instance is running. False on a success means the key found a finished instance. |
| `WorkflowApprovalCancelResult(Success, AlreadyTerminal, FinalOutcome, ErrorCode, Error)` | A finished instance answers `Success=false, AlreadyTerminal=true` with its outcome. |
| `WorkflowApprovalStatusDto` | Status `InProgress`, `Approved`, `Rejected`, `Cancelled`; the current step and role; every decision; `ApprovalUrl`. PLAN-46 adds `Reason` and `CorrelationId` at the end. |
| `WorkflowApprovalErrorCodes` | `DefinitionMissing` (422), `PolicyDenied` (reserved), `EngineUnavailable` (timeout, 5xx, network, no address), `Forbidden` (401/403), `Conflict` (409), `Invalid` (400, or no tenant). Each has an Arabic message (`MessageFor`). |
| `IWorkflowOutcomeDecided` (schema 1) + `WorkflowStepDecisionDto` | The outcome event, published through HR.Core's outbox. |
| `WorkflowOutcomeGuard` | `ShouldApply` (schema, a known outcome, the link, the pending state) and `HasExcludedDecider`; counts what it ignores in `workflow_outcome_ignored_total{entity_type,reason}`. |
| `WorkflowApprovalSubmission` | Submit and withdraw, the same for every owner (rules 2 to 5 below). |
| `WorkflowApprovalSweepJob<TStore>` + `IWorkflowApprovalOwnerStore` | The owner-side sweep (rule 7). `AddWorkflowApprovalSweep<TStore>`. |
| `FakeWorkflowApprovalClient` (`HRManagement.TestSupport/Workflow`) | The engine for an owner's tests. |

**HR.Core's side.**

- The registry entries `CardRequest` (`svc-idcard`, `ID-CARD-ISSUANCE`, audience `idcard`, `/ui/idcard/`), `DisciplinaryAction`
  (`svc-compliance`, `DISCIPLINARY-APPROVAL`, `compliance`, `/ui/compliance/`) and `SeparationRecord` (`svc-hr-contracts`,
  `SEPARATION-APPROVAL` and `SEPARATION-APPROVAL-SENSITIVE`, `compliance`, `/ui/hr-contracts/`) have `OutcomeEvent = Generic`.
- `ExternalOutcomePublisher` stages `IWorkflowOutcomeDecided` inside the decision's `OutboxUnitOfWork`: on the last approval and on a
  rejection (`WorkflowInstanceActionService`), and on a withdrawal (`ExternalApprovalService.CancelAsync`). `Decisions` come from
  `StepHistoryJson` with the roles of the pinned definition; the message id comes from the instance; the tenant header from the
  instance.
- `GET api/workflow-instances/{id}/external-status` answers the owner that started the instance (`SourceClientId` equal to the
  caller); anyone else gets the 404 of an unknown id.
- `WorkflowGuardrails`: a card has one step or more, each `idcard` or `hr-management-approver`; discipline has two or more, the first
  `compliance`, the last `hr-management-approver`, distinct approvers; separation has two or more, the last
  `hr-management-approver`, and `-SENSITIVE` holds `compliance`.
- `idcard` and `compliance` act on steps (`WorkflowStepAction`) and read the inbox (`HrCoreWorkInbox`); neither is in `ModuleHrCore`
  or `HrCoreReadOnly`, so they reach the inbox and the generic page only.
- No type is exempt from the step role (`ExemptEntityTypes = []`).
- `WorkflowPackSeeder.EnsureCrossServiceChainsAsync` raises `ID-CARD-ISSUANCE` («مراجعة مكتب البطاقات», `idcard`) and
  `DISCIPLINARY-APPROVAL` («مراجعة امتثال» `compliance`, «اعتماد صاحب الصلاحية» `hr-management-approver`, distinct approvers) at
  every start. `HIRE-WIZARD`, `TRANSFER-APPROVAL`, `IT-TICKET` and `SEPARATION-WIZARD` are retired (`RetiredCodes`).

**The rules.**

1. The requester never travels in the body: HR.Core takes it from `X-Acting-User`, from the owning service only. A job sets
   `ActingUserScope`.
2. The key is pinned per submission: `"{EntityType}:{Id}:{SubmissionNo}"`, where the number is the one after the last submission
   that reached the engine. A retry after a failure uses the same number and finds the same instance. A key whose instance had
   finished applies that outcome through `GetStatusAsync`; one whose instance was withdrawn before the record pointed at it is spent,
   and the next number is used.
3. A failed start changes nothing: the record stays a draft and the person reads why. `PendingApproval` and the instance id are set
   in the same save.
4. A save that fails after a start withdraws the instance on a best-effort basis («فشل الحفظ المحلي»), unless the record already
   points at it (a concurrent submission of the same record).
5. A withdrawal goes through `CancelAsync` first. `AlreadyTerminal` applies the final outcome; the record never returns to draft.
6. Delivery is at least once. The owner applies an outcome only to the record linked to the instance while it is pending; its own
   closed state wins over a late outcome, which is logged and counted.
7. Every owner registers `WorkflowApprovalSweepJob<TStore>` for its type: every `WorkflowApproval:Sweep:IntervalMinutes` (10; 1 on
   the acceptance stack) one replica (advisory lock) walks the tenants from a source outside row security (`AuditLogs`), sets
   `TenantIdFlow` before each scope opens a connection, and applies the finished outcome of each record waiting longer than
   `StaleAfterMinutes` (15; 2 on the acceptance stack) with the consumer's own `ApplyOutcomeAsync`. It counts what it applies in
   `workflow_approval_sweep_applied_total{entity_type}`.
8. An owner that publishes in turn (Compliance → `IDisciplinaryActionTaken`) does it in the transaction of the status change, through
   the consumer outbox inside the consumer and the bus outbox from the sweep, with a message id from the record.
9. Schema 1; an owner ignores a newer version.

**The owners today.** IDCard: `CardRequestApprovalService`, `CardRequestApprovalOutcomeConsumer`, `CardRequestApprovalStore`.
Compliance: `DisciplinaryApprovalService`, `DisciplinaryApprovalOutcomeConsumer`, `DisciplinaryApprovalStore`.
`WorkflowDefinitionsHaveHandlersTests` finds each consumer by reflection as an `IConsumer<IWorkflowOutcomeDecided>`; PLAN-60 adds
`SeparationRecord → SeparationApprovalOutcomeConsumer` and removes its pending mark.

## API Endpoints

### Definitions
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/workflow-definitions` | List definitions (filter by entityType, publishedOnly) |
| GET | `/api/workflow-definitions/{id}` | Get definition |
| GET | `/api/workflow-definitions/antiforgery` | Token for the writes below (admin policy) |
| POST | `/api/workflow-definitions` | Create a draft (auto-increments version) |
| PUT | `/api/workflow-definitions/{id}` | Update a draft |
| POST | `/api/workflow-definitions/{id}/submit` | Validate and submit for publication |
| POST | `/api/workflow-definitions/{id}/publish` | Publish, by a second person |
| POST | `/api/workflow-definitions/{id}/return` | Return a submitted definition to draft |
| POST | `/api/workflow-definitions/{id}/retire` | Retire a published version, with a reason |
| DELETE | `/api/workflow-definitions/{id}` | Delete a draft |

Reads: `WorkflowDefinitionRead` (the HR.Core readers and `workflow-admin`). Everything else: `WorkflowDefinitionAdmin` and an antiforgery token.

### Instances
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/workflow-instances` | List instances (filter by entityType, status) |
| GET | `/api/workflow-instances/{id}` | Get instance |
| POST | `/api/workflow-instances` | Start, for the service that owns the type (`WorkflowExternalStart`) |
| POST | `/api/workflow-instances/{id}/cancel` | Withdraw a running instance, the owner only |
| GET | `/api/workflow-instances/{id}/external-status` | The state of an instance, for the service that started it (`WorkflowExternalStart`, PLAN-46) |
| POST | `/api/workflow-instances/{id}/approve` | Approve current step (`WorkflowStepAction`) |
| POST | `/api/workflow-instances/{id}/reject` | Reject workflow (`WorkflowStepAction`) |
| GET | `/api/workflow-instances/{id}/history` | Get step history |

## Legacy path
`ApprovalWorkflowService` remains for `ApprovedCV` until wave 2 moves it onto the engine. A step needs its `RoleRequired`, or `hr-admin` under `Workflow:AdminBypass`, and only the requester or `hr-admin` cancels (PLAN-44, G-WF-19). The bridge `CreateViaEngineOrLegacy` had no caller and was removed.

## Database Tables
- `WorkflowDefinitions` — indexed on (Code, Version) unique + (DomainEntityType, IsPublished)
- `WorkflowInstances` — indexed on (EntityType, EntityId) + Status + DefinitionId
- `WorkflowDelegations` — dropped by `HCM050_ApprovalDelegationScope` (PLAN-50); delegations live in `ApprovalDelegations`

Migration: `20260731161000_WF001_WorkflowEngine`

## Testing
See `Wf001WorkflowEngineTests.cs`:
- Sequential approval through all steps
- Rejection at any step
- Version pinning (instance retains original version)
- Condition evaluation (step skipping)

## Validation
Run `scripts/wf001-workflow-validate.ps1` to verify:
- All required source files exist
- Migration file present
- Test file present
- SharedKernel types compile
