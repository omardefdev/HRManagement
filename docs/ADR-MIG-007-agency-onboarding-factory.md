# ADR-MIG-007 — Agency onboarding factory (gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-007 (card 72) |
| Date | 2026-07-30 |
| Dependencies | MIG-006 Satisfied; STR-004 Satisfied; EXT-006 NotStarted; PROD-002 NotStarted |

## Context

MIG-006 provides cutover/rehearsal contracts. STR-004 forbids per-agency product forks. There is no signed agency onboarding factory for readiness assessment, standard playbook, sandbox/certification stubs, wave sign-off gates, required data/security/ops owners, lead-time/defect telemetry, and exit/rollback — without claiming EXT-006 developer portal or PROD-002 self-admin.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/mig-onboarding/onboarding-matrix.json` |
| Depth | **Agency onboarding gate pack** — contracts + dual-agency lab harness; not live portal/self-admin |
| Nofork | Required; joins STR-004 |
| Wave | Sign-offs required before wave |
| Soft blockers | agency-extract-access; live-dual-agency-onboard; ONB-001 |
| Closure | Done (agency onboarding gate pack) |
| Out of scope | EXT-006 portal; PROD-002 self-admin; ONB-001 provision engine; Domain rewrite |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| MIG-007a | Live dual-agency onboarding |
| MIG-007b | EXT-006 sandbox/portal bind |
| MIG-007c | PROD-002 self-serve |
| MIG-007d | ONB-001 four-eyes provision |

## Blocker register

| Id | Status | Note |
|---|---|---|
| MIG-006 | Satisfied | Cutover gate Done |
| STR-004 | Satisfied | Nofork policy Done |
| EXT-006 | NotStarted | Developer sandbox/portal later |
| PROD-002 | NotStarted | Agency self-admin later |
| ONB-001 | Blocked | Signed provision / four-eyes |
| agency-extract-access | Blocked | Lab harness only |
| live-dual-agency-onboard | Blocked | Live E2E later |

## Consequences

- ONB-002 and national go-live packs consume readiness/wave criteria from this pack.
- Validate fails on fork attempts, wave without sign-off, missing owners, or lead time/defects over budget.
