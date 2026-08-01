# ADR-EXT-006 — Sandbox / certification / developer portal (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-006 (card 78) |
| Date | 2026-07-30 |
| Dependencies | EXT-005 Satisfied; REL-001 Satisfied |

## Context

EXT-005 provides governed API/exchange contracts. REL-001 provides staging/synthetic. There is no signed contract for partner sandbox onboarding without prod access, ephemeral credentials, synthetic-only data, conformance that rejects incompatible clients, certification before prod join, revocation, docs matching published contracts, or rate/error simulation — without claiming a live developer portal.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-sandbox/sandbox-cert-matrix.json` |
| Depth | Lab sandbox-cert gate pack + harness; not live portal |
| Soft blockers | live-developer-portal; real-pii-in-sandbox; partner-cert-fleet-live; EXT-007 |
| Closure | Done as pack foundation; production binding supersedes for ops |

## Later stories

| Story | Scope |
|---|---|
| EXT-006a | Live developer portal |
| EXT-006b | Partner cert fleet live |
| EXT-006c | Production key rotation |
| EXT-006d | Live usage analytics |
| EXT-006e | MIG-007 EXT-006 Satisfied flip |
