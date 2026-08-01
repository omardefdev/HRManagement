# ADR-MIG-005 — Evidence production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-005 (card 70) |
| Date | 2026-07-30 |
| Supersedes | Reconciliation/evidence gate pack as insufficient for production |
| Dependencies | MIG-004 production match binding |

## Context

The prior pack published the recon matrix, accounted buckets, financial/dual-control/evidence contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigEvidence` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-evidence/recon-matrix.json` |
| Runtime | `MigEvidenceOptions` + `IMigEvidenceProvider` + `IAgencyReconClaimGuard` |
| Accounted | Migrated / Rejected / Deferred; reason required for Rejected+Deferred |
| Financial | unexplainedVarianceForbidden; tolerance 0; payroll control required |
| Dual control | minSigners ≥ 2; distinct roles |
| Evidence | immutable package; sha256; required artifacts |
| Live recon | Default `LiveAgencyReconClaim=false`; forbidden until MIG-005a–d |
| Soft blockers | MIG-003/OBS-003 Satisfied; agency-extract-access/live-agency-recon/live-independent-sql Blocked |
| Closure | **Done (production evidence binding)** |

## Consequences

- Staging/Production fail startup on incomplete recon policy, dishonest blockers, or live agency-recon claim.
- CI runs `mig005-evidence-runtime-validate.ps1`.
- Live recon jobs, EVD-003 vault, independent SQL, and MIG-006 cutover rehearsals remain out of scope.
