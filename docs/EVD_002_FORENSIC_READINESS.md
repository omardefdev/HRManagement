# EVD-002 — Forensic Readiness & Evidence Preservation

**Card:** EVD-002  
**Status:** Done  
**Priority:** P2-National / L  
**Owner:** CSIRT + Legal

## Overview

Establishes forensic readiness capabilities so that when a security incident, fraud,
or legal dispute occurs, digital evidence can be collected, preserved, and presented
in a legally admissible manner. Builds on EVD-001's evidence hash chain with case
management, custody tracking, and volatile evidence capture.

## Forensic Readiness Requirements

### Evidence Collection

1. **Automated forensic logging** — all security-relevant events captured with tamper-evident hash chain (EVD-001).
2. **Volatile evidence capture** — memory dumps, active sessions, network state snapshotable on demand.
3. **Immutable snapshots** — point-in-time database and file system snapshots for investigation.

### Case Management (ForensicCase)

Each investigation is tracked as a `ForensicCase` with:

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Case identifier |
| `TenantId` | Guid | Agency boundary |
| `CaseNumber` | string | Human-readable case reference |
| `Title` | string | Case summary |
| `Severity` | enum | Critical/High/Medium/Low |
| `Status` | enum | Open/InProgress/Closed/Archived |
| `OpenedAt` | DateTimeOffset | When case was opened |
| `ClosedAt` | DateTimeOffset? | When case was closed |
| `LeadInvestigator` | string | Primary investigator |
| `LegalHoldActive` | bool | Whether legal hold is in effect |

### Custody Chain

See `docs/evd/custody-chain-model.json` for the structured custody transfer model.

Every evidence item tracks:
1. **Collector** — who/what collected the evidence
2. **Timestamp** — when collected (verified time source)
3. **Hash** — SHA-256 integrity hash at collection
4. **Custody transfers** — every handoff logged with from/to/reason/timestamp
5. **Storage location** — sovereign, encrypted, access-controlled

### Legal Hold

When a legal hold is active:
- No deletion or modification of relevant records
- Retention policies suspended for held data
- Employee records with `LegalHold=true` protected (DATA-002)
- Hold scope documented and reviewed quarterly

### Analyst Access

- Investigators access evidence through read-only interface
- No production system access granted for investigation
- Clean-room analysis environment for sensitive cases
- All analyst actions logged in evidence chain

## Breach Scoping

When a breach is detected:
1. Identify affected data and individuals within SLA
2. Preserve volatile evidence before remediation
3. Scope analysis using evidence chain correlation
4. Document findings with custody-chain integrity

## Third-Party Evidence

External evidence (law enforcement requests, regulatory submissions):
- Verified against hash and timestamp
- Chain of custody maintained through handoff protocol
- Format: sealed JSON package with signatures

## Verification

- Insider threat investigation exercise
- Ransomware recovery evidence preservation drill
- Evidence handoff to legal/regulatory simulation
- Legal hold enforcement verification

## Files

- `SharedKernel/Evidence/ForensicCase.cs` — case entity
- `docs/evd/custody-chain-model.json` — custody transfer model
- `scripts/evd002-validate.ps1` — artifact validation
- `Evd002ForensicTests.cs` — xUnit tests
