# ADR-EXT-004 — National PKI / signatures / timestamps (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-004 (card 76) |
| Date | 2026-07-30 |
| Dependencies | STR-002 Satisfied; OBS-003 Satisfied; SOV-003 Satisfied (key-sovereignty binding; live HSM = SOV-003a) |

## Context

STR-002 provides the control catalog. OBS-003 provides tamper-evident audit/hash/WORM. EXT-003 closed with lab stub signatures and soft-blocks live PKI on EXT-004. Platform form signatures (`ContractSignaturesController`) are deletable UI records — not legal evidence. `OfficialSealRecord` is metadata only. There is no signed contract for signer intent/role, cert validation/revocation fail-closed, trusted timestamp, LTV after cert expiry, live HSM key custody, clock skew, algorithm agility, or prohibition of treating form signatures as legal truth. SOV-003 production key-sovereignty binding is Satisfied; live HSM remains SOV-003a / EXT-004b (`hsm-prod` Blocked). SYD-011 remains OPEN-NO-GO for CA/trust-chain legality.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-pki/pki-signature-matrix.json` |
| Depth | **PKI/signature contract gate pack** — contracts + mock CA/TSA/HSM harness; not live national CA/HSM/TSA |
| Port join | Form signatures gap + EXT-003 `livePkiRequiresExt004` + OBS-003 evidence (no new Domain/Web port) |
| Soft blockers | SOV-003; national-pki-live; hsm-prod; tsa-live; SYD-011 |
| Closure | Done (PKI/signature contract gate pack) |
| Out of scope | Live CA; prod HSM; live TSA; SIG-001 UI; EXT-003e live bind; Domain rewrite; SYR-011 |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| EXT-004a | National CA bind |
| EXT-004b | HSM production |
| EXT-004c | TSA live |
| EXT-004d | SIG-001 join |
| EXT-004e | EXT-003e treasury signature bind |
| EXT-004f | SYD-011 alignment |

## Blocker register

| Id | Status | Note |
|---|---|---|
| STR-002 | Satisfied | Control catalog Done |
| OBS-003 | Satisfied | Tamper audit Done |
| SOV-003 | Satisfied | Key-sovereignty binding Done; live HSM = SOV-003a |
| national-pki-live | Blocked | Lab mock CA only |
| hsm-prod | Blocked | Mock HSM; keys must not leave HSM in contract |
| tsa-live | Blocked | Mock TSA; outage scenario required |
| SYD-011 | Blocked | Legal OPEN-NO-GO; not closed by this pack |

## Consequences

- SIG-001 / EXT-003e / SYR-011 consume signer-intent, LTV, revocation, and HSM custody contracts from this pack.
- Validate fails on form-signature-as-legal, revoked/expired-without-LTV as valid, key exfil, or wrongly Satisfied soft blockers.
- EXT-003 treasury matrix keeps EXT-004=NotStarted until EXT-003e (do not break Ext003 tests).
