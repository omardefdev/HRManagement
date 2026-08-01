# SYR-002 — Agency and Workforce Census Register

## Purpose

Establish an authoritative, reconciled census of Syrian government agencies, employees, pensioners, and legacy systems. This census feeds workload sizing, capacity planning, and migration scope.

## Scope

- Certified enumeration of ministries, agencies, institutions, companies, and administrative units
- 14 governorates with sub-district breakdown
- Employees by type (permanent/temporary/daily/seasonal/contract), legal regime (Law 50/53/special), and agency
- Pensioners and beneficiaries by institution (Social Insurance / Insurance & Pensions)
- Monthly payroll file sizes and payment channels
- System users, branches, and document volumes
- Source, date, and uncertainty percentage for every figure
- Reconciliation between Finance, Administrative Development, insurance, pensions, and individual agencies

## Census Register Structure

See `docs/syr/census-register.json` for the canonical data structure.

### Key Dimensions

| Dimension | Source Authority |
|-----------|----------------|
| Agencies | Ministry of Administrative Development |
| Governorates | Central Bureau of Statistics |
| Employees | Agency HR + Ministry of Finance payroll files |
| Pensioners | Social Insurance Institution + Insurance & Pensions Institution |
| Payroll volumes | Ministry of Finance treasury records |

### Classification Categories

- `current` — active employees on payroll
- `historical` — separated employees with archived records
- `beneficiary` — pension/insurance beneficiaries
- `duplicate` — identified duplicates pending resolution

### Headroom Policy

Capacity planning uses a minimum 50% headroom above peak census figures, or a higher value set by the national authorization board.

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_002_CENSUS.md` | This document |
| `docs/syr/census-register.json` | Census register data structure |
| `scripts/syr002-validate.ps1` | Validates SYR-002 artifacts and structure |
| `Syr002CensusTests.cs` | Unit tests for census register schema and validation |

## Acceptance Criteria Mapping

- [x] Reconcile between Finance, Administrative Development, insurance, pensions, and agencies
- [x] Explain differences in published official figures
- [x] Classify current/historical/beneficiary/duplicate
- [x] Convert census to workload and capacity plan
- [x] Headroom ≥ 50% for peak or board-approved higher value
- [x] Source, date, and uncertainty for every figure

## Validation

- `scripts/syr002-validate.ps1`
- `Syr002CensusTests`
