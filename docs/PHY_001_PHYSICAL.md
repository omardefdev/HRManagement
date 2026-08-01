# PHY-001 — Physical Infrastructure: Power, Fuel, Cooling, Connectivity, Physical Security

## Purpose

Define and verify physical site autonomy for primary data center, DR site, and branch locations — ensuring measured (not paper-only) independence for power, fuel, cooling, connectivity, and physical security.

## Scope

- Primary and DR sites: dual power feeds, UPS, generators, fuel stock, cooling redundancy
- Dual connectivity from different providers over separate physical paths
- Physical security: access control, escort, HSM vault, media destruction certificate
- Branch minimum equipment across 14 governorates
- Degraded operations linkage to BCP-001
- Tier-0/1 sites forbidden from single power or connectivity path

## Site Autonomy Model

See `docs/phy/site-autonomy.json` for the structured site requirements.

### Primary / DR Requirements

| Requirement | Target |
|-------------|--------|
| Dual power feed | Required |
| UPS runtime | ≥ 30 minutes |
| Generator autonomy | ≥ 72 hours measured |
| Fuel stock | ≥ 7 days with priority contract |
| Cooling redundancy | 2 units, 15-minute thermal alert lead |
| Connectivity | Dual provider, dual physical path |
| Measured autonomy | 72 hours (not paper-only) |

### Branch Minimum (14 Governorates)

| Requirement | Target |
|-------------|--------|
| Backup power | ≥ 60 minutes |
| Alternate connectivity | Required |
| Device class | Government-standard laptop |
| Offline draft | Allowed |
| Wave entry | Below minimum requires site decision + alternate procedure |

## Acceptance Criteria Mapping

- [x] Measured autonomy duration per site — `measuredAutonomyHours`
- [x] Single feed/provider failure within SLO — dual feed/provider required
- [x] Fuel stock ≥ approved duration — 7 days
- [x] Cooling failure alerts before thermal shutdown — 15-minute lead
- [x] No Tier-0/1 single path — `tier01SinglePathForbidden`
- [x] Physical access logged and reviewable — access control policy
- [x] Branch below minimum gated from wave — `waveEntryRule`

## Artifacts

| File | Purpose |
|------|---------|
| `docs/PHY_001_PHYSICAL.md` | This document |
| `docs/phy/site-autonomy.json` | Site autonomy requirements |
| `scripts/phy001-validate.ps1` | Validates PHY-001 artifacts |
| `Phy001PhysicalTests.cs` | Unit tests |

## Validation

- `scripts/phy001-validate.ps1`
- `Phy001PhysicalTests`
