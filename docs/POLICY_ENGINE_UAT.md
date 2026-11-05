# PROD-001a/b — Policy Engine UAT

## Enforce API

| ID | Scenario | Expected |
|----|----------|----------|
| P1 | POST `/api/internal/policies/enforce` as `svc-admin-logistics`, Assets Assign privileged | RequireEvidence + TriggerWorkflow; 200 for an allow and for a denial, with `effectiveMode` |
| P2 | Shadow mode | Ledger entry + shadowDiff; the caller gets the Off decision, the server's is in `shadowDecision` |
| P3 | Publish same actor | 400 four-eyes |
| P4 | Simulate synthetic facts | Decision without enforce side effects |
| P5 | Enforce, HR.Core unreachable | Refused with the failure message (`FailClosed`, the default) |
| P6 | Shadow, HR.Core unreachable | Not blocked; `policy_client_failure_total` grows and a warning is logged |
| P7 | An interactive user on either enforce route | No route, or 403 |

The server derives mode, actor and tenant; the client owns the domain (`PolicyDomainOwners`). See [ADR-S2S-042](ADR-S2S-042-trust-boundary.md).

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
