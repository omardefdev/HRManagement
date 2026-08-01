# ADR-EXT-006 — Sandbox-cert production binding

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-006 (card 78) |
| Date | 2026-07-30 |
| Supersedes | Pack-only foundation as insufficient for production |
| Dependencies | EXT-005 Satisfied; REL-001 Satisfied |

## Context

EXT-006 had no prior pack. This binding creates the sandbox-cert matrix and loads it at host startup/CI so Staging/Production cannot claim a live portal, real PII, manual secrets, or certified incompatible clients.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/ext-sandbox/sandbox-cert-matrix.json` |
| Runtime | `ExtSandboxOptions` + `IExtSandboxProvider` + `ISandboxCertClaimGuard` |
| Live portal | Default `LiveSandboxPortalClaim=false` until EXT-006a–e |
| Soft blockers | live-developer-portal / real-pii-in-sandbox / partner-cert-fleet-live Blocked; EXT-007 NotStarted |
| Closure | **Done (production sandbox-cert binding)** |

## Consequences

- Startup/CI fail on incomplete policy, dishonest blockers, or live portal claim.
- MIG-007 keeps EXT-006=NotStarted until EXT-006e.
- EXT-005 keeps partner-cdc-cert=Blocked until live cert fleet.
