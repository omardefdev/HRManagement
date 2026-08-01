# STR-006 — Reconciliation checklist

| # | Check | Evidence | OK |
|---|---|---|---|
| R1 | ADR Accepted, card 64 | ADR-STR-006 | |
| R2 | ≥6 components with ownership quad | ownership-matrix | |
| R3 | Critical busFactor ≥ 2 | busFactorRule | |
| R4 | Skills Tier-0 minQualified ≥ 2 | skills[] | |
| R5 | Succession + KT linked | succession / KT docs | |
| R6 | Staffing stub; no 24x7 claim | STAFFING_ONCALL | |
| R7 | Budget funds ops/security/backup 5y | BUDGET_5Y | |
| R8 | TCO role stub (not FIN-001) | TCO_APPROVAL | |
| R9 | Vendors have RTO/disclosure/Exit | VENDOR_SLA | |
| R10 | IP + EOL documented | IP_SOURCE, EOL | |
| R11 | Fixtures pass/fail as labeled | testdata/str006 | |
| R12 | Soft blockers correct | SOV-007/FIN-001/SRE-001 | |
| R13 | Validate harness PASS | str006-ownership-validate.ps1 | |

**Soft blockers (intentional):** SOV-007 Satisfied (vendor-exit binding; live drill = VEN-001); FIN-001 NotStarted; SRE-001 Blocked.
