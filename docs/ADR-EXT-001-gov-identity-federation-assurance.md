# ADR-EXT-001 — Government identity federation and assurance (hybrid gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-001 (card 73) |
| Date | 2026-07-30 |
| Dependencies | IAM-003 Satisfied; IAM-004 Satisfied; STR-002 Satisfied; IAM-001 Blocked |

## Context

IAM-003/004 provide SoD and service identity. STR-002 provides legal/control catalog context. Platform Keycloak/OIDC baseline exists but there is no signed national federation/assurance policy for persona×AAL/FAL mapping, IdP trust inventory, phishing-resistant MFA for sensitive roles, revocation SLA, break-glass, citizen/employee separation, attribute authority rules, or conformance harnesses. IAM-001 (JWT audience/TLS production hardening) remains open.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-federation/assurance-matrix.json` |
| Depth | **Hybrid federation/assurance gate pack** — contracts + lab harness; not live IdP bind |
| Soft blockers | national-idp-live; agency-idp-live; phishing-mfa-fleet; live-federation-conformance |
| Closure | Done (federation/assurance gate pack) |
| Out of scope | Live national/agency IdP; WebAuthn fleet; IAM-005; EXT-002; KeycloakExtensions rewrite |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| EXT-001a | National IdP bind |
| EXT-001b | Agency IdP broker |
| EXT-001c | Phishing-MFA rollout |
| EXT-001d | Live federation conformance |

## Blocker register

| Id | Status | Note |
|---|---|---|
| IAM-003 | Satisfied | SoD Done |
| IAM-004 | Satisfied | S2S identity Done |
| STR-002 | Satisfied | Catalog pack Done |
| IAM-001 | Blocked | JWT audience / prod TLS still open |
| national-idp-live | Blocked | Lab stubs only |
| agency-idp-live | Blocked | Lab stubs only |
| phishing-mfa-fleet | Blocked | Policy only |
| live-federation-conformance | Blocked | Lab harness only |

## Consequences

- EXT-002 civil-registry integration consumes assurance and identifier policy from this pack.
- Validate fails on name-only linking, revocation SLA breach, missing persona assurance, or citizen/employee mix.
