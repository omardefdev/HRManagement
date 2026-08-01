# ADR-PROD-007 — Extensibility platform / stable API (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | PROD-007 (card 86) |
| Date | 2026-07-30 |
| Dependencies | INT-002/SUP-001/STR-004 Satisfied |

## Context

No signed contract for supported APIs/events/webhooks, N-2 compatibility, plugin isolation, quotas, deprecation, SDK policy, and no-direct-DB / no-custom-fork — without claiming live extension runtime.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/prod-extensibility/extensibility-matrix.json` |
| Depth | Lab extensibility gate pack + harness; not live plugin host |
| Soft blockers | live-extension-runtime / live-partner-sdk / live-webhook-delivery Blocked; EXT-006/IOP-001/REL-004 NotStarted |
| Closure | Pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| PROD-007a | Live extension/plugin runtime |
| PROD-007b | Live partner SDK |
| PROD-007c | Live webhook delivery |
| PROD-007d | EXT-006 certification surface |
| PROD-007e | REL-004 / upgrade ops |
