# ADR-PROD-005 — Unified workbox / search / notifications (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-005 (card 84) |
| Date | 2026-07-30 |
| Dependencies | INT-003 Satisfied; EXT-007 Satisfied; IAM-002 NotStarted (honest soft blocker) |

## Context

No signed contract for unified workbox (tasks/cases/approvals), safe tenant/org search, bulk four-eyes limits, secret-free notifications with receipts, and deep-link reauth — without claiming live inbox or live notification delivery.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-workbox/workbox-matrix.json` |
| Depth | Lab workbox gate pack + harness; not live UI |
| Soft blockers | IAM-002 NotStarted; live-workbox-ui / live-notification-delivery / live-safe-search Blocked; COM-001/WF-002/NAT-005 NotStarted; PROD-006 Satisfied |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-005a | Live workbox UI |
| PROD-005b | Live notification delivery |
| PROD-005c | Live safe search |
| PROD-005d | COM-001 / WF-002 join |
| PROD-005e | NAT-005 / PROD-006 join |
