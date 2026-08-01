# MIG-002 — Journey matrix

Source: [`mig-canonical/code-map-matrix.json`](mig-canonical/code-map-matrix.json) — [ADR-MIG-002](ADR-MIG-002-canonical-model-code-maps.md).

## CanonicalModel

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FiveEntities | Yes | data-architecture | employee/org/job/payroll/document |
| ProvenanceFlags | Yes | data-architecture | provenanceRequired |

## CodeMapCoverage

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DomainsCovered | Yes | data | requiredCoverageDomains |
| DispositionRequired | Yes | qa | Map\|Transform\|Quarantine |

## NoMassOther

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ForbidOther | Yes | audit | unmappedPolicy |
| MassOtherFails | Yes | qa | mass-other-fail |

## LegacyIdReference

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ReferenceOnly | Yes | security | legacyIdPolicy |
| AuthKeyFails | Yes | qa | legacy-id-as-auth-key-fail |

## RoundTrip

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| StatusOrgSamples | Yes | qa | status-org-roundtrip |

## LegalBusinessSignOff

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SignedMapping | Yes | legal-hr-finance | signing |
| SignoffFixture | Yes | qa | legal-business-signoff |

Soft blockers: agency-extract-access, SYR-003, MDM-001 = Blocked.
