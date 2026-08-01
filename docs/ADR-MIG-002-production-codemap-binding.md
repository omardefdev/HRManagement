# ADR-MIG-002 — Code-map production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-002 (card 67) |
| Date | 2026-07-30 |
| Supersedes | Canonical/code-map gate pack as insufficient for production |
| Dependencies | MIG-001 production inventory binding |

## Context

The prior pack published the code-map matrix, disposition rules, legacy-id policy, and pack validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigCanonical` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-canonical/code-map-matrix.json` |
| Runtime | `MigCanonicalOptions` + `IMigCanonicalProvider` + `ICodeMapClaimGuard` |
| Dispositions | Map / Transform / Quarantine only; Map requires `targetValue` |
| Mass Other | `forbidOther=true` + forbidden catch-alls + `otherBucketMaxRate` |
| Signing | `signedByRole` ≠ `reviewerRole`; `mappingVersion` + `contentHash` required |
| Legacy id | Reference-only; EmpNo/NAT_ID never auth/session/tenant key |
| Live engine | Default `LiveCodeMapEngineClaim=false`; forbidden until MIG-003 |
| Soft blockers | MIG-001/TEN-001/GOV-003 Satisfied; agency-extract-access/SYR-003/MDM-001 Blocked |
| Closure | **Done (production codemap binding)** |

## Consequences

- Staging/Production fail startup on incomplete dispositions, mass-Other policy gaps, broken signing, dishonest blockers, or live code-map engine claim.
- CI runs `mig002-codemap-runtime-validate.ps1`.
- Live transform worker (MIG-003), identity match (MIG-004), live agency value coverage, SYR-003 legal regimes, and MDM-001 job catalog remain out of scope.
