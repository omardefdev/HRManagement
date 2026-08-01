# ADR-EXT-002 — Civil registry / person proofing (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-002 (card 74) |
| Date | 2026-07-30 |
| Dependencies | EXT-001 Satisfied; GOV-001 Satisfied; MIG-004 Satisfied |

## Context

EXT-001 provides federation/assurance contracts. GOV-001 provides classification/privacy/DPIA. MIG-004 provides identity match/NID exception policy. Platform already has Level-2 `INationalIdentityVerifier` stub/file adapters, but there is no signed civil-registry contract for purpose-bound consent, minimal query/response fields, mismatch human workflow, cache/expiry, unavailable fail-closed mode, rate limits, query audit, or anti-bulk-harvest rules.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-civil/civil-registry-matrix.json` |
| Depth | **Civil-registry contract gate pack** — contracts + mock/sandbox harness; not live MOI API |
| Port join | `INationalIdentityVerifier` stub/file (no Domain rewrite) |
| Soft blockers | moi-civil-registry-live; live-sandbox-cert; L10N-002; national-idp-live |
| Closure | Done (civil-registry contract gate pack) |
| Out of scope | Live MOI connector; bulk harvest tooling; EXT-003; Domain rewrite |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| EXT-002a | Live MOI bind |
| EXT-002b | Certified sandbox |
| EXT-002c | Production rate/audit sink |
| EXT-002d | Correction UI |

## Blocker register

| Id | Status | Note |
|---|---|---|
| EXT-001 | Satisfied | Federation gate Done |
| GOV-001 | Satisfied | Privacy/DPIA Done |
| MIG-004 | Satisfied | Match/merge gate Done |
| moi-civil-registry-live | Blocked | Lab mock only |
| live-sandbox-cert | Blocked | Stub/file only |
| L10N-002 | NotStarted | Arabic normalization deferred |
| national-idp-live | Blocked | Inherited from EXT-001 |

## Consequences

- Hire/proofing and FRD/MDM flows consume purpose/minimal/audit contracts from this pack.
- Validate fails on purpose mismatch, excess field storage, outage-as-truth, bulk harvest, or stale-as-truth.
