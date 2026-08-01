# ADR-STR-002 — Legal / control catalog for the target state

| Field | Value |
|---|---|
| Status | Accepted |
| Card | STR-002 (card 60) |
| Date | 2026-07-30 |
| Dependencies | STR-001 Done; GOV-001 Done; GOV-002 Done |
| Inputs | file 12 Syria baseline; file 14 SYD OPEN-NO-GO; legal-decisions/register.json |

## Context

GOV-002 provides a payroll/service legal **decision** register (ApprovedInterim). GOV-001 covers classification/privacy evidence. There is no national cross-domain **control catalog** linking local requirements to cards, evidence, tests, owners, frequency, and exceptions. File 12 facts and file 14 OPEN-NO-GO decisions are not machine-rows. LEGAL_TBD is gated for payroll release only.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/str-legal/control-catalog.json` — journeys DomainCoverage / ControlLinkage / ConflictExceptions / LegislativeWatch / TraceWalkthrough |
| Depth | **Legal control catalog pack** — not SYR-001 gazette corpus, not live SOV-001 ATO (baseline binding separate) |
| Domains | civil_service, labor, payroll, tax, pension, privacy, archive, signature_evidence, accessibility, sovereignty |
| Row shape | requirementId, domain, title, sourceRef, confidence A–D, cardIds, controlOwner, evidencePath, testRef, frequency, exceptionId, status Implemented\|Partial\|Blocked-Legal |
| Payroll | Point at GOV-002 register / LD-* — do not duplicate decisions |
| File 14 | Sample Blocked-Legal / confidence D rows (not invent signed law) |
| LEGAL_TBD | Implemented rows must not reference LEGAL_TBD; Blocked-Legal allowed; keep `check-no-legal-tbd.py` for payroll |
| Closure | Done = pack + validate + Str002*; TRACEABILITY `STR-002 ✓ (catalog pack)` |
| Out of scope | Full SYR-001 corpus, MoF table import, NIST ATO, new rule-engine code |

## Consequences

- Downstream SYR-001 / SOV / PRV consume this catalog as the control index.
- Confidence D without Blocked-Legal or exception fails the validate harness.
- Claiming national legal closure without this pack is forbidden (EXCEPTION).
