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

## What the engine governs (PLAN-49)

One catalog, `PolicyDomainCatalog`, says which domains the engine governs. The validator, the engine, the internal endpoint, the
start-up check, the policy page and [POLICY_DOMAIN_REGISTRY.md](POLICY_DOMAIN_REGISTRY.md) all read it. Five domains are governed
and run in Shadow in Production, Staging and the prod-like stack (`Core.Recruitment`, `Core.Establishment`, `Assets.Custody`,
`IDCard.Lifecycle`, `Contracts.Leave`); the fifteen others are out of scope and always Off (D-POL-1, D-POL-13).

| ID | Scenario | Expected |
|----|----------|----------|
| G1 | Open `/PolicyTemplates` as a policy author | The catalog table: five governed domains with their effective mode (Shadow), their call sites and their last ledger decision; fifteen out of scope, each with its reason; a draft link only for a governed domain |
| G2 | Create a draft for a domain out of scope (the page, or `POST api/policies/drafts`) | Refused with «المجال خارج نطاق المحرك» (400 `DOMAIN_OUT_OF_SCOPE` in the API); nothing is written |
| G3 | Start a service with `PolicyEngine__<a domain out of scope>=Shadow` | The service does not start: "… must be Off: the domain is out of the policy engine's scope" (`scripts/pol049-outofscope-mode-boot-check.ps1`) |
| G4 | Add a rule outside the domain's capabilities: an effect nobody consumes, a `SetField` of a field the domain does not own, a `TriggerWorkflow` the entity type does not own or that is not bound to its action | 400 with an Arabic message and the JSON path (`THEN_EFFECT_NOT_ALLOWED`, `THEN_SETFIELD_NOT_ALLOWED`, `THEN_WORKFLOW_NOT_ALLOWED`) |
| G5 | Leave: create on the HR page, request on the self-service portal, edit, approve | Four Shadow ledger rows with `CallerClientId=svc-hr-contracts`, each decided on facts that match the schema; nothing is blocked |
| G6 | A self-service leave over an approved one | Created `Rejected`, with «مرفوض آلياً: …» at the head of its notes; never dropped |
| G7 | Process a card request; hand over a privileged asset; submit an establishment change | One Shadow ledger row each (`svc-idcard`, `svc-admin-logistics`, no caller client for HR.Core's own domain); the outcome is the local one |
| G8 | Enforce (the acceptance overlay only): a rule fires the ordinary offer approval for an exception offer | The submission is refused with «السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء»; the policy's instance is cancelled; the offer stays a draft |

Shadow records the engine's decision in the ledger and changes no outcome. The calling service times every Shadow call (one log
line `policy_client_call … elapsed_ms=`, and the histogram `policy_client_call_duration_ms`). Moving a domain to Enforce is a wave-2
decision (D-POL-13). Extending the engine to another domain means a descriptor in the catalog, a call site and a seed change set.

## PROD-001 closure

- `live-policy-engine` Satisfied when harness passes
- `live-workflow-runtime` Satisfied when TriggerWorkflow creates instances
- `LivePolicyEngineClaim=true` in staging post-UAT
