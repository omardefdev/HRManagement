# MIG-001 — Journey matrix

Source: [`mig-inventory/source-inventory.json`](mig-inventory/source-inventory.json) — [ADR-MIG-001](ADR-MIG-001-legacy-source-inventory-field-sot.md).

## SourceCatalog

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| UniverseCovered | Yes | migration-office | knownSourceUniverse ⊆ sources |
| TypesEncoded | Yes | migration-office | database/file/paper/api/unsupported |

## FieldSot

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CriticalTargetsMapped | Yes | data | targetFieldSot |
| Gov003Join | Yes | data | field-ownership joins |

## MeasuredVolumeQuality

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| NoEstimateOnly | Yes | qa | volume.estimateOnly=false or Exception |
| QualityProfiled | Yes | qa | quality + profiling |

## AccessRisks

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RisksDocumented | Yes | security | accessRisks[] |

## SamplingProfiling

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SampleManifest | Yes | qa | sampling fixture |
| AgencySignedProfile | Yes | agency-hr | agencySigned=true |

## CoverageGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| HundredPercentOrException | Yes | migration-office | exceptions for Unsupported |

Soft blockers: agency-extract-access, SYR-002 = Blocked.
