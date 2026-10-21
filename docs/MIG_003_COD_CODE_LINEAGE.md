# MIG-003 — COD Code Lineage (Hybrid Strategy)

Legacy business codes are **preserved as-is** during ETL load. New records after COD-001 use the dynamic code engine. Sequence counters are bootstrapped post-load so new codes do not collide with historical values.

## Principles

| Rule | Detail |
|------|--------|
| **Preserve** | Migrated rows keep legacy `OrgUnit.Code`, `TagNumber`, `ReferenceNumber`, etc. |
| **No GenerateAsync on load** | MIG-003 transform workers map legacy fields directly; they do not call `/api/codes/generate`. |
| **Counter seed only** | Post-load bootstrap sets `CodeSequenceCounters.LastValue` from `MAX(parsed sequence)` in existing data. |
| **Immutability** | Application controllers reject manual edits to issued codes (COD-001). |

## Entity disposition matrix

| EntityKind | Table | Column | MIG disposition | Counter bootstrap |
|------------|-------|--------|-----------------|-------------------|
| `OrgUnit` | `OrgUnits` | `Code` | Map legacy `ORG_CODE` as-is | Per parent prefix (`ROOT.01`) |
| `FundedPosition` | `FundedPositions` | `PositionCode` | Map legacy `POSITION_CODE` as-is | Per org-grade scope |
| `AssetTag` | `Assets` | `TagNumber` | Map legacy `ASSET_TAG` as-is | `AH-YYYY-######` pattern |
| `ItAssetTag` | `UserDevices` | `AssetTag` | Map legacy tag as-is | `IT-YYYY-######` pattern |
| `ArchiveReference` | `ArchiveItems` | `ReferenceNumber` | Map legacy `ARCH_REF` as-is | Per `EntityPrefix` + year |
| `LegalCase` | `LegalCases` | `CaseNumber` | Map legacy `CASE_NO` as-is | `LC-YYYY-#####` pattern |
| `ServiceTransaction` | `ServiceTransactions` | `ReferenceNumber` | Map legacy `TXN_REF` or null | Per `TypeCode` + year |
| `CitizenReference` | `CitizenApplications` | `ReferenceNumber` | Map as-is | `CIT-YYYY-######` |
| `IdCardSerial` | `IssuedCards` | `SerialNumber` | Map as-is | `ID-YYYY-#######` |

## Excluded from bootstrap

Demo/dev prefixes are ignored when seeding counters:

- `DEMO-*`, `IT-DEV-*`, `LEG-*`

## Code maps (MIG-002)

See [`mig-canonical/code-map-matrix.json`](mig-canonical/code-map-matrix.json) for legacy source field bindings.

## Bootstrap script

After legacy load (or on production cutover):

```powershell
./scripts/mig003-cod-sequence-bootstrap.ps1 -RepoRoot .
```

This runs idempotency tests and validates lineage documentation. For HRCore tables, use `CodSequenceBootstrapService.BootstrapHRCoreAsync()` from an admin job or integration test harness.

For remote service DBs (Logistics, IT, Archive, etc.), export observed codes and call:

```csharp
await bootstrap.BootstrapFromObservedCodesAsync(tenantId, CodeEntityKind.AssetTag, tagNumbers);
```

## Related docs

- [COD_001_CODE_GENERATION.md](COD_001_CODE_GENERATION.md)
- [MIG_002_CODE_MAPS.md](MIG_002_CODE_MAPS.md)
- [MIG_003_READINESS_CHECKLIST.md](MIG_003_READINESS_CHECKLIST.md)
