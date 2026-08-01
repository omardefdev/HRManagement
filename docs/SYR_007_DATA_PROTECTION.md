# SYR-007 — Law 12/2024 Personal Data Protection Compliance

## Purpose

Ensure full compliance with Syrian Personal Data Protection Law No. 12 of 2024, mapping all processing activities to lawful bases, implementing data subject rights workflows, and maintaining accountability evidence.

## Scope

- Controller and processor role identification
- Lawful basis determination for each processing activity
- Sensitive data handling with additional safeguards
- ROPA (Record of Processing Activities) maintenance
- Privacy notices at collection points
- Data subject rights: access, rectification, erasure, objection, portability, restriction
- DPIA for high-risk processing
- DPO registration with Personal Data Protection Authority
- Breach notification workflow (72-hour authority notification)
- Data sharing and sub-processor agreements
- Retention and deletion per legal requirements
- No consent coercion when legal basis is public duty
- Cross-border transfer restrictions (SYR-008 sovereignty)
- Complaint handling and evidence preservation

## Compliance Matrix

The full machine-readable compliance matrix is at `docs/syr/law12-2024-compliance-matrix.json`.

### Legal Bases

| ID | Basis | Applicable To |
|----|-------|---------------|
| LB-01 | Public duty / legal obligation | Employment records, payroll, social insurance, tax |
| LB-02 | Contractual necessity | Employment contract processing |
| LB-03 | Legitimate interest | Security, fraud prevention, analytics |
| LB-04 | Consent | Optional services (not coerced) |

### Data Subject Rights

| ID | Right | Deadline |
|----|-------|----------|
| DSR-01 | Right to be informed | At collection / 30 days |
| DSR-02 | Right of access | 30 days |
| DSR-03 | Right to rectification | 15 days |
| DSR-04 | Right to erasure | 30 days (subject to retention) |
| DSR-05 | Right to object | 15 days |
| DSR-06 | Right to data portability | 30 days |
| DSR-07 | Right to restrict processing | Immediate |

### Compliance Controls

| ID | Control | Evidence |
|----|---------|----------|
| CC-01 | ROPA maintained | PRV_001_ROPA.md |
| CC-02 | Privacy notices | Application UI |
| CC-03 | DPIA for high-risk | PRV_001_DPIA_*.md |
| CC-04 | DPO registered | Registration letter |
| CC-05 | DSR workflow | GOV-001 DSAR |
| CC-06 | Breach notification ≤72h | PRV_003_BREACH_RESPONSE.md |
| CC-07 | Data sharing agreements | PRV_001_DATA_SHARING.md |
| CC-08 | Retention schedule | SYR-015 matrix |
| CC-09 | No consent coercion | Legal basis mapping |
| CC-10 | No cross-border transfer | SYR-008 sovereignty |
| CC-11 | Processor agreements | PRV_001_SUBPROCESSOR_REGISTER.md |
| CC-12 | Security measures | SOV-001 + HARD-001 |

## Key Differences from GDPR

- Deadlines sourced from Law 12/2024 text, not EU regulation
- Authority is Syrian Personal Data Protection Authority (not EU DPA)
- Licensing/authorization requirements per Syrian law
- No consent basis when processing is a public duty (prevents coercion)
- Cross-border transfer governed by SYR-008 national hosting requirements

## Acceptance Criteria Mapping

- [x] Certified law and instructions received — law text referenced
- [x] License/authorization number or written confirmation — CC-04
- [x] DPO registered — CC-04
- [x] Every field/purpose/recipient/duration in ROPA — CC-01
- [x] No forced consent for public duty processing — CC-09
- [x] Deadlines from law text, not GDPR — DSR table
- [x] Complaint and evidence handling end-to-end — CC-05, CC-06

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_007_DATA_PROTECTION.md` | This document |
| `docs/syr/law12-2024-compliance-matrix.json` | Machine-readable compliance matrix |
| `scripts/syr007-validate.ps1` | Validates SYR-007 artifacts |
| `Syr007DataProtectionTests.cs` | Unit tests |

## Validation

- `scripts/syr007-validate.ps1`
- `Syr007DataProtectionTests`
