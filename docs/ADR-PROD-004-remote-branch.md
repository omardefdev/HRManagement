# ADR-PROD-004 — Remote branch / weak connectivity / languages (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-004 (card 83) |
| Date | 2026-07-30 |
| Dependencies | UX-002 Satisfied; DATA-001 Satisfied; FILE-001 Satisfied |

## Context

No signed contract for remote-branch UX under low bandwidth: performance budgets, throttled labs, resumable uploads, drafts, idempotent retry, AR/EN + RTL/LTR, limited encrypted offline with threat model, WCAG 2.2 AA — without claiming live PWA fleets or field offline.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-branch/remote-branch-matrix.json` |
| Depth | Lab remote-branch gate pack + harness; not live fleets |
| Soft blockers | live-remote-branch-ux / live-pwa-fleet / live-offline-field Blocked; L10N-001/MOB-001/PROD-005/SYR-013 NotStarted |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-004a | Live remote branch UX |
| PROD-004b | Live PWA fleet / resumable uploads |
| PROD-004c | Live field offline |
| PROD-004d | L10N-001 / MOB-001 join |
| PROD-004e | SYR-013 / NAT join |
