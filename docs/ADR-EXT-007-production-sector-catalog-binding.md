# ADR-EXT-007 — Sector-catalog production binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-007 (card 79) |
| Date | 2026-07-30 |
| Dependencies | EXT-005 Satisfied; STR-003 Satisfied |

## Context

EXT-007 had no prior pack. This binding creates the sector-catalog matrix and loads it at host startup/CI so Staging/Production cannot claim live adapters, implicit critical integrations, sensitive PII in notifications, or swallowed failures.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-sector/sector-catalog-matrix.json` |
| Runtime | `ExtSectorOptions` + `IExtSectorProvider` + `ISectorCatalogClaimGuard` |
| Live catalog | Default `LiveSectorCatalogClaim=false` until EXT-007a–e |
| Soft blockers | live-sector-adapters / live-notification-channel / adapter-fleet-certified-live Blocked; PROD-005/COM-001 NotStarted |
| EXT-006 sync | sandbox matrix blocker EXT-007 → Satisfied |

## Consequences

- Lab harness only until live-sector-adapters is raised.
- Live adapters, live notification, and PROD-005/COM-001 joins remain out of scope.
