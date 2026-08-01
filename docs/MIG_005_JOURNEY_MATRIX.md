# MIG-005 — Journey matrix

Source: [`mig-evidence/recon-matrix.json`](mig-evidence/recon-matrix.json) — [ADR-MIG-005](ADR-MIG-005-migration-reconciliation-evidence.md).

## FullAccounted

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| BucketsDefined | Yes | migration-qa | accountedBuckets |
| FullAccountedPass | Yes | qa | full-accounted-pass |
| UnexplainedGapFail | Yes | qa | unexplained-gap-fail |

## FinancialControls

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PayrollPolicy | Yes | migration-qa | financialPolicy |
| VarianceFail | Yes | qa | financial-variance-fail |

## BusinessSamples

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SamplingPolicy | Yes | migration-qa | sampling |
| BusinessSignoff | Yes | agency-data-owner | business-sample-signoff |
| StatisticalSample | Yes | qa | statistical-sample-pass |

## ImmutableEvidence

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PackageContract | Yes | internal-audit | evidencePackage |
| TamperFail | Yes | qa | tamper-evidence-fail |

## ChecksumRecon

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ChecksumPass | Yes | qa | checksum-recon-pass |

## DualControlSignOff

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DualPolicy | Yes | internal-audit | dualControl |
| MissingDualFail | Yes | qa | dual-control-missing-fail |
| WaveSignoff | Yes | migration-qa | agency-wave-signoff |

Soft blockers: agency-extract-access, live-agency-recon, live-independent-sql = Blocked.
