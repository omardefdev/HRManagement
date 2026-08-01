# ADR-WF-001: Configurable Workflow Engine

## Status
Accepted

## Context
The HR Management platform requires a configurable, versioned workflow engine that can handle sequential approval flows, condition-based step gating, delegation, and domain guardrails. Different agencies need different approval chains without code forks. The existing `ApprovalWorkflowService` handles basic multi-step approval but lacks:
- Configurable step definitions stored as data
- Version pinning (instances retain their definition version)
- Condition-based step skipping
- Delegation within workflow context
- Effective-date control for definitions

## Decision
Implement a lightweight, built-in workflow engine (WF-001) with the following architecture:

1. **SharedKernel/Workflow/** — Enums (`WorkflowStatus`, `WorkflowStepStatus`, `WorkflowStepType`) + DTOs (`WorkflowDefinitionSnapshot`, `WorkflowStepSnapshot`, `WorkflowCondition`, `WorkflowStepHistoryEntry`)
2. **HR.Core.Domain/Entities/Workflow/** — EF entities (`WorkflowDefinition`, `WorkflowInstance`, `WorkflowDelegation`) inheriting `BaseEntity`
3. **ServiceDefaults/Workflow/** — `IWorkflowEngine` interface + `WorkflowEngine` implementation + `AddWorkflowEngine` DI extension
4. **Controllers** — REST API for definition CRUD/publish and instance lifecycle (start/approve/reject/history)
5. **Bridge** — `ApprovalWorkflowService.CreateViaEngineOrLegacy` creates both legacy `ApprovalRequest` and optionally a `WorkflowInstance`, enabling gradual migration

### Version Pinning
When a workflow instance is created, it records the definition's `Version`. Even if the definition is later upgraded, in-flight instances continue using their original version.

### Condition Evaluator
`WorkflowCondition` evaluates simple field-operator-value expressions against a JSON context dictionary. Supports: Equals, NotEquals, GreaterThan, LessThan, GreaterThanOrEqual, Contains, In.

### Guardrail Roles
Definitions include `GuardrailRolesJson` listing roles that constitute domain invariant constraints. These can be enforced at the controller level before allowing configuration changes.

## Consequences
- Agencies can configure approval workflows without code changes
- Existing `ApprovalRequest` path continues working (backward compatible)
- New workflows can be gradually migrated using the bridge method
- Step definitions stored as JSON avoid schema migrations for workflow changes
- Engine is stateless and testable (operates on snapshots)

## Alternatives Considered
- External workflow engine (Elsa, Temporal) — rejected for sovereignty/simplicity
- BPMN parser — over-engineered for current sequential/conditional needs
- Database-stored C# scripts — security risk
