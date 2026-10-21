# ADR-PROD-001b — Unified workflow binding

## Status

Accepted — policy effects include `TriggerWorkflow`; `PolicyOrchestrator` starts `WorkflowInstance` in same enforce call.

## Decision

- Effect `TriggerWorkflow` with param `workflowCode`
- `PolicyWorkflowBinding` maps domain + code → `WorkflowDefinition`
- Replaces separate `WorkflowTriggerService` path when policy returns `WorkflowInstanceId`
- Fallback to legacy trigger when policy does not require workflow

## Consequences

- Asset assign uses unified enforce first (Admin.Logistics)
- Workflow definitions must exist and be published (seeded: ASSET-ASSIGN-APPROVAL, ASSET-LOSS-APPROVAL)
