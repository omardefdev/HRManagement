# ADR-MIG-001 — Inventory production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | MIG-001 (card 66) |
| Date | 2026-07-30 |
| Supersedes | Inventory gate pack as insufficient for production |
| Dependencies | STR-007 production value binding |

## Context

The prior pack published the source inventory, field SoT, sampling/profiling fixtures, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `MigInventory` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/mig-inventory/source-inventory.json` |
| Runtime | `MigInventoryOptions` + `IMigInventoryProvider` + `ILegacyExtractClaimGuard` |
| Coverage | knownSourceUniverse fully present; Unsupported/ExceptionSigned need signed exception |
| Inventoried | estimateOnly forbidden; accessRisks ≥ 1 |
| Field SoT | requiredTargetFields mapped with gov003Join |
| Live extract | Default `LiveAgencyExtractClaim=false`; forbidden while agency-extract-access Blocked |
| Soft blockers | agency-extract-access/SYR-002 Blocked; STR-001/GOV-003 Satisfied |
| Closure | **Done (production inventory binding)** |

## Consequences

- Staging/Production fail startup on incomplete coverage, estimate-only Inventoried sources, broken SoT, or live-extract claim.
- CI runs `mig001-inventory-runtime-validate.ps1`.
- Live agency PII extract, official census volumes, and MIG-003 ETL remain out of scope.
