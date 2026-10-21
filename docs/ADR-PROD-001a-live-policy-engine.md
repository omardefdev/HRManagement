# ADR-PROD-001a — Live policy engine

## Status

Accepted — implements PROD-001a live policy engine with declarative rules, versioning, and enforcement ledger.

## Decision

- Central store in **HRCore** (`PolicyTemplate`, `PolicyTemplateVersion`, `PolicyRule`, `PolicyFactsSchema`, `PolicyEnforcementLedger`)
- Evaluation kernel in **SharedKernel** (`PolicyEvaluator`, condition groups AND/OR/NOT)
- Runtime API: `POST /api/policies/enforce`, `POST /api/policies/evaluate`
- Admin UI: `/PolicyTemplates`
- No arbitrary code in rules — closed effect enum only

## Consequences

- Migration `PROD001ab_ComprehensivePolicyEngine` on `hr_core_db`
- Services integrate via `IPolicyEnforceClient` (ServiceDefaults)
- Shadow → Enforce rollout per domain via `PolicyEngine:{Domain}` config
