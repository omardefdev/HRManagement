# STR-001 — Journey matrix

Source: [`str-scope/envelope-matrix.json`](str-scope/envelope-matrix.json) — [ADR-STR-001](ADR-STR-001-national-scope-envelope.md).

## NationalScope

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CountryCharter | Yes | program | STR_001_SCOPE_CHARTER.md |
| GeographyWaves | Yes | program | 14 governorates |
| CriticalJourneys | Yes | product | FUN-001..006 + payroll |

## QuantitativeEnvelope

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| NationalCompetitionRows | Yes | architecture | envelope = national-competition |
| SyriaInterimRows | Yes | architecture | envelope = syria-interim |
| OwnerDateSource | Yes | program | per-indicator fields |

## WorkloadDataset

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DatasetDescribed | Yes | architecture | STR_001_WORKLOAD_DATASET.md |
| SeasonsMapped | Yes | sre | NFR seasons link |
| LabScaleSeparated | Yes | sre | scale_factor not CAP proof |

## AssumptionsExclusions

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AssumptionRegister | Yes | program | STR_001_ASSUMPTION_REGISTER.md |
| Exclusions | Yes | product | STR_001_EXCLUSIONS.md |
| Syr002Blocked | Yes | program | assumption ASS-SYR-CENSUS |

## GrowthReviewSignOff

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| Growth2x | Yes | architecture | STR_001_GROWTH_2X_SCENARIO.md |
| IndependentReview | Yes | program | assessor or waiver |
| BoardSignOff | Yes | program | STR_001_UAT_PACK.md |
