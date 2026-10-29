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

## API Endpoints

### Definitions
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/workflow-definitions` | List definitions (filter by entityType, publishedOnly) |
| GET | `/api/workflow-definitions/{id}` | Get definition |
| POST | `/api/workflow-definitions` | Create new definition (auto-increments version) |
| PUT | `/api/workflow-definitions/{id}` | Update draft definition |
| POST | `/api/workflow-definitions/{id}/publish` | Publish definition |
| DELETE | `/api/workflow-definitions/{id}` | Delete draft definition |

### Instances
| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/workflow-instances` | List instances (filter by entityType, status) |
| GET | `/api/workflow-instances/{id}` | Get instance |
| POST | `/api/workflow-instances` | Start new instance |
| POST | `/api/workflow-instances/{id}/approve` | Approve current step |
| POST | `/api/workflow-instances/{id}/reject` | Reject workflow |
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
