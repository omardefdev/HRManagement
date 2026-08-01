# ADR-EXT-007 — Sector integrations catalog (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-007 (card 79) |
| Date | 2026-07-30 |
| Dependencies | EXT-005 Satisfied; STR-003 Satisfied |

## Context

EXT-005 provides governed API/exchange contracts. STR-003 provides module catalog binding. There is no signed contract for sector adapters (pension/insurance/tax/attendance/ERP/budget/mail-notify) requiring declared criticality, contract/SLO/retry/reconciliation/exit, notification without sensitive PII, visible failures, certification before enable, and delivery receipts — without claiming live adapters.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-sector/sector-catalog-matrix.json` |
| Depth | Lab sector-catalog gate pack + harness; not live adapters |
| Soft blockers | live-sector-adapters; live-notification-channel; adapter-fleet-certified-live; PROD-005; COM-001 |
| Closure | Done as pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| EXT-007a | Live sector adapters |
| EXT-007b | Live notification channel |
| EXT-007c | Adapter fleet certified live |
| EXT-007d | PROD-005 join |
| EXT-007e | COM-001 join |
