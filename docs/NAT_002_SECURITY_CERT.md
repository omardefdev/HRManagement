# NAT-002 — Independent Security & Privacy Certification

**Card:** NAT-002  
**Status:** Done (certification harness + evidence template)  
**Priority:** National Gate / L  
**Owner:** Independent Assessor + DPO/CISO

## Purpose

Define the independent security and privacy certification pack: control assessment, pen/red team, privacy impact, sovereignty, supplier, incident, remediation, and residual risk sign-off — scoped to release digest with limited-duration ATO conditions.

## Scope

- Control assessment crosswalk to SOV-001..007 production bindings
- Pen/red team evidence from SOV-006
- Privacy impact and legal approvals (PRV-001..003)
- Sovereignty attestations (SOV-002..004)
- Supplier security and incident readiness
- Remediation closure and retest evidence
- Residual risk signed by independent authority, not developer
- ATO expiry and conditions tracking

## Data Model

See `docs/nat/nat002-checklist.json` for structured certification evidence template.

## Business Rules

1. **SOV-006 binding Satisfied** — pen/red harness complete; live assessment Blocked.
2. **Zero open blockers in harness** — live assessor report and ATO grant remain Blocked.
3. **Privacy/legal approvals required** — DPO and Legal sign-off slots defined.
4. **Certification scoped** — release digest, environment, and 12-month validity window.
5. **Residual risk authority sign-off** — independent authority, not engineering.

## Relationship to SOV-001..007 / OPS-006

| Card | Focus |
|------|-------|
| SOV-001..007 | Production sovereignty/security bindings Done |
| OPS-006 | Cyber recovery clean-room binding Done |
| NAT-002 | Independent certification evidence template |

## Acceptance Criteria Mapping

- [x] Security/privacy certification checklist with SOV references
- [x] Evidence domains for control, pen/red, privacy, sovereignty
- [x] ATO expiry and conditions template
- [x] Residual risk signatory role defined (authority not developer)
- [x] Live assessor report and ATO = Blocked until scheduled

## Artifacts

| File | Purpose |
|------|---------|
| `docs/NAT_002_SECURITY_CERT.md` | This document |
| `docs/nat/nat002-checklist.json` | Security/privacy certification checklist |
| `scripts/nat002-validate.ps1` | Validates NAT-002 artifacts |
| `Nat002SecurityCertTests.cs` | Unit tests for certification schema |

## Validation

- `scripts/nat002-validate.ps1`
- `Nat002SecurityCertTests`

## Remaining Live Blockers

- Independent assessor-issued signed report pending
- Limited-duration ATO grant pending assessment
- NAT-001 evidence repository NotStarted
