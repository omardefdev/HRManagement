# ADR-PROD-001 — Policy-platform production binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-001 (card 80) |
| Date | 2026-07-30 |
| Dependencies | PAY-002 Satisfied; GOV-002 Satisfied; STR-002 Satisfied |

## Context

PROD-001 had no prior pack. This binding creates the policy-platform matrix and loads it at host startup/CI so Staging/Production cannot claim a live engine, publish invalid policies, simulate on real data, or bypass national mandatory controls via override.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-policy/policy-platform-matrix.json` |
| Runtime | `ProdPolicyOptions` + `IProdPolicyProvider` + `IPolicyPlatformClaimGuard` |
| Live engine | Default `LivePolicyEngineClaim=false` until PROD-001a–e |
| Soft blockers | live-policy-engine / live-workflow-runtime / live-form-designer Blocked; PROD-002 NotStarted; ONB-001 Blocked |
| STR-004 sync | nofork matrix blocker PROD-001 → Satisfied; SoftBlockedIds = ONB-001 only |

## Consequences

- Lab harness only until live-policy-engine is raised.
- Live engine/workflow/form and WF-001/WF-002 remain out of scope.
