# NAT-001 — National Control Evidence Pack

**Card:** NAT-001
**Status:** NOT-CLOSED — structure defined, evidence not yet collected
**Priority:** P0-National / XL
**Owner:** Assurance Office + National Authorization Board

> **Why this is not Done.** This document defines the *shape* of the evidence pack that a national
> authorization will require. Defining the shape is not the same as holding the evidence: the pack
> itself has not been produced, and no assessor has signed it. Marking the card Done made a
> template look like proof. See [بروتوكول التدقيق](AUDIT_PROTOCOL.md).

## Overview

Defines the structure, content, and lifecycle of the national control evidence pack — the authoritative bundle that proves every control, capability, and gate condition has been met before national production authorization. The pack is reproducible, independently verifiable, and stored in the sovereign evidence repository (EVD-003).

## Control Evidence Index

The machine-readable index is at `docs/nat/control-evidence-index.json`.

### Evidence Pack Structure

| Section | Content |
|---------|---------|
| Control Baseline | SOV-001 compliance matrix with per-control evidence links |
| Security Assessment | SOV-005/006 threat model and pen test reports |
| Data Sovereignty | SOV-002 data flow map and residency attestation |
| Performance | CAP-001 soak/stress/spike results |
| Operational Readiness | SRE-002 ORR checklists per service |
| Privacy | PRV-001 ROPA + DPIAs |
| Hardening | HARD-001 baseline scan results |
| Disaster Recovery | OPS-006 clean-room rebuild evidence |
| Fraud Controls | FRD-001 anomaly engine audit reports |
| UAT Sign-off | UAT-001 signed acceptance per capability |

### Evidence Requirements

| Requirement | Description |
|-------------|-------------|
| Immutability | Evidence cannot be altered after collection |
| Reproducibility | ≥10% of evidence items independently reproducible by external assessor |
| Hash chain | Every item hashed (SHA-256) and linked to EVD-001 chain |
| Freshness | Evidence expires after configurable period; expired items create visible gap |
| Provenance | Collector, timestamp, source, and release version recorded |
| Classification | Each evidence item classified (confidential/restricted/internal) |
| PII protection | No PII or secrets in evidence store; redaction enforced |

### Authorization Gate

- Evidence pack submitted to National Authorization Board
- Independent assessor verifies ≥10% sample
- Zero critical gaps allowed; non-critical gaps require signed POA&M
- Authorization is time-limited and scope-limited (per wave)
- Re-authorization required after major change or scope expansion

## Pack Lifecycle

1. **Collection** — Automated from CI/CD, test runs, security scans, operational drills
2. **Validation** — Schema and completeness check
3. **Submission** — Sealed pack with manifest and top-level hash
4. **Assessment** — Independent review with sampling
5. **Decision** — Authorize / Authorize-with-conditions / Reject
6. **Maintenance** — Ongoing evidence refresh; gap monitoring

## Dependencies

EVD-001, EVD-003, SOV-001, SOV-002, SOV-005, SOV-006, CAP-001, SRE-002, PRV-001, HARD-001, FRD-001, UAT-001

## Artifacts

- `docs/nat/control-evidence-index.json`
- `scripts/nat001-validate.ps1`
- `Nat001EvidenceTests`
