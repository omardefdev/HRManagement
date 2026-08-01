# ADR-PROD-001 — Policy / workflow / form platform (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-001 (card 80) |
| Date | 2026-07-30 |
| Dependencies | PAY-002 Satisfied; GOV-002 Satisfied; STR-002 Satisfied |

## Context

STR-002 provides control catalogs. STR-004 forbids agency forks and points policy/workflow/form surfaces at PROD-001. There is no signed contract for versioned templates, effective dating, four-eyes publish, synthetic simulation, rollback, tenant override within national ceiling, or prohibition of arbitrary code — without claiming a live policy engine.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-policy/policy-platform-matrix.json` |
| Depth | Lab policy-platform gate pack + harness; not live engine |
| Soft blockers | live-policy-engine; live-workflow-runtime; live-form-designer; PROD-002; ONB-001 |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-001a | Live policy engine |
| PROD-001b | Live workflow runtime |
| PROD-001c | Live form designer |
| PROD-001d | Live agency override product |
| PROD-001e | WF-001/WF-002 / ApprovalStep |
