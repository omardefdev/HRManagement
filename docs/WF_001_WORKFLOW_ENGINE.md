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
  ├── WorkflowInstance.cs
  └── WorkflowDelegation.cs

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

### Delegation
`WorkflowDelegation` allows a principal to delegate workflow actions to another user, optionally scoped to a specific workflow code or entity type, with time bounds.

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
  `WorkflowGuardrails`, known condition operators) and returns every error in Arabic. The validator runs again at `publish`.
- `publish` needs a person who is not the creator, the submitter or the last editor.
- A published version is never edited: a change is a new version, and running instances stay on the version they pinned. A new
  version keeps the entity type of its code.
- `publish` warns when a `PolicyWorkflowBinding` pins another version.
- `retire` needs a reason and refuses the last published version of a type the engine starts, unless a replacement is published.
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
- The latest valid published version of the code starts; a policy binding has no say. None: 422.
- `POST {id}/cancel` withdraws (`Cancelled`, history `Withdrawn`), the owner only; a finished instance is 409.
- The policy engine only says an approval is needed: `PolicyWorkflowTriggerAllowlist` is empty and `PolicyOrchestrator` starts no
  registered type.
- Custody events are schema v2: `WorkflowInstanceId` comes from the instance. Their consumers apply an outcome only when it equals
  the id on the record, and ignore a v1 message, counting both in `workflow_outcome_ignored_total`.

### Separation of duties (PLAN-43, G-WF-06)
The step role is enforced for every type except `Workflow:StepRole:ExemptEntityTypes`
(`docs/qa/workflow-step-role-exemptions.json`, each with an owner plan and a date). The old key is ignored with a start-up warning.

`WorkflowApprovalEvaluator` runs: service account refused → structure → role → `WorkflowSodRules` → entity gate → initiator rule.
`WorkflowPanelBuilder` calls the same rules:

- `Excluded`: the requester and `ExcludedApproverUserIds`, approving and rejecting.
- `Subject`: `SubjectEmployeeId`, approving and rejecting.
- `DistinctApprover`: approving only, when `RequireDistinctApprovers` is true (new definitions and `ASSET-ASSIGN-APPROVAL`; not the
  national recruitment and establishment ones).

`Workflow:AdminBypass` never passes these rules.

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
| POST | `/api/workflow-instances/{id}/approve` | Approve current step (`WorkflowStepAction`) |
| POST | `/api/workflow-instances/{id}/reject` | Reject workflow (`WorkflowStepAction`) |
| GET | `/api/workflow-instances/{id}/history` | Get step history |

## Bridge (Dual-Path)
`ApprovalWorkflowService.CreateViaEngineOrLegacy(...)` creates both:
1. A legacy `ApprovalRequest` (always — maintains backward compatibility)
2. Optionally a `WorkflowInstance` when a `WorkflowDefinitionSnapshot` is provided

This enables gradual migration: callers can pass a definition snapshot when one exists, or `null` to use legacy-only path.

## Database Tables
- `WorkflowDefinitions` — indexed on (Code, Version) unique + (DomainEntityType, IsPublished)
- `WorkflowInstances` — indexed on (EntityType, EntityId) + Status + DefinitionId
- `WorkflowDelegations` — indexed on (PrincipalId, DelegateId, ActiveFrom) + (IsRevoked, ActiveUntil)

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
