# MIG-001 — Program adoption minutes (inventory production binding)

| Field | Value |
|---|---|
| Card | MIG-001 (card 66) |
| Date | 2026-07-30 |
| Decision | Bind source-inventory into host startup and CI |
| Default live extract | `LiveAgencyExtractClaim=false` |

## Operational assets

| Asset | Path |
|---|---|
| Matrix | `docs/mig-inventory/source-inventory.json` (copied to `MigInventory/`) |
| Negative fixtures | estimate-only-volume, target-field-no-sot, undocumented-access-risk |
| Positive fixtures | sampling, counts, agency-signed profiling, signed-unsupported |

Promotion to live agency extract = close agency-extract-access. Official census volumes = SYR-002. Live ETL = MIG-003+.

## Signatories (roles)

| Role | Name / org | Signature / date |
|---|---|---|
| Migration Office | _pending wet-ink_ | |
| Data Architecture | _pending wet-ink_ | |
| Agency IT (pilot) | _pending wet-ink_ | |

## Independent review

See [MIG_001_INDEPENDENT_REVIEW_WAIVER.md](MIG_001_INDEPENDENT_REVIEW_WAIVER.md).
