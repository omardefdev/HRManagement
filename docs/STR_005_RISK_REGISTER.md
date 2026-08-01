# STR-005 — Program risk register

Machine source: [`str-governance/risk-register.json`](str-governance/risk-register.json).

Distinct from QA risk-to-test matrix (`QA_RISK_TO_TEST_MATRIX.md`).

| riskId | likelihood | impact | owner | linked decision | status |
|---|---|---|---|---|---|
| RK-STR005-LAW-LAG | Medium | High | legal | PG-LAW-2026-07-SIM | Open |
| RK-STR005-INCIDENT-DRIFT | Medium | High | security | PG-INC-2026-07-SIM | Open |
| RK-STR005-DELAY-SILENT | Low | Critical | program | PG-DELAY-2026-07-SIM | Mitigated |
| RK-STR005-SYD017-NAMES | High | Medium | sponsor | PG-GOLIVE-GATE-POLICY | Accepted |

Verification: after each simulation (law / incident / delay), linked risks must appear here and in the decision log.
