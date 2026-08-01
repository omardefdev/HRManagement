# STR-007 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 65 | ADR-STR-007 | |
| R2 | ≥9 KPIs with required fields | kpi-matrix | |
| R3 | owner ≠ auditor | kpis[] | |
| R4 | Baselines frozen pre-pilot | baselines[] | |
| R5 | Forbidden screen-count benefits | forbiddenBenefitMeasures | |
| R6 | Privacy tenant + min cell | privacyRules | |
| R7 | Independent measurement fixture | PASS | |
| R8 | Sample reconcile pass/fail | fixtures | |
| R9 | Quarterly review sans screen-count | PASS | |
| R10 | Soft blockers FIN-001/PROD-003 | blockers | |
| R11 | Hard deps Satisfied | STR-001/003/GOV-003 | |
| R12 | Validate harness PASS | str007-value-kpi-validate.ps1 | |

**Soft blockers:** FIN-001=NotStarted; PROD-003=Blocked.
