# PROD-001a/b — Policy Engine UAT

## Enforce API

| ID | Scenario | Expected |
|----|----------|----------|
| P1 | POST `/api/policies/enforce` Assets Assign privileged | RequireEvidence + TriggerWorkflow |
| P2 | Shadow mode | Ledger entry + shadowDiff; behavior unchanged |
| P3 | Publish same actor | 400 four-eyes |
| P4 | Simulate synthetic facts | Decision without enforce side effects |

## Assets integration

| ID | Scenario | Expected |
|----|----------|----------|
| A1 | Assign privileged without evidence | Blocked |
| A2 | Enforce + workflow | WorkflowInstanceId on assignment |
| A3 | PolicyEngine:Off | Static shim / legacy rules |

## Domain registry

See HRCore `/PolicyTemplates` — migration status per domain.

## PROD-001 closure

- `live-policy-engine` Satisfied when harness passes
- `live-workflow-runtime` Satisfied when TriggerWorkflow creates instances
- `LivePolicyEngineClaim=true` in staging post-UAT
