# STR-005 — Simulation pack

Walkthroughs for card verification (law change / incident / delay). Fixtures under `testdata/str005/scenarios/`.

## 1. Law change (`law-change.json`)

1. Trigger: interim payroll rule before gazette.
2. Open `LawChange` decision: owner **legal**, reviewer **architecture**, board Architecture.
3. ADR required → reference ADR-STR-005 (or dedicated law ADR later).
4. Declare exception `EX-STR005-SIM-LAW-INTERIM` with `expiresOn`.
5. Add/update risk `RK-STR005-LAW-LAG`.
6. Note GOV-002 handoff when gazette arrives.
7. Validate: owner ≠ reviewer; exception not expired.

## 2. Incident (`incident.json`)

1. Trigger: security/ops incident requiring emergency change.
2. Decision: owner **security**, reviewer **change**, board Security.
3. Align with REL-003 Emergency path; set post-review date.
4. Exception `EX-STR005-SIM-INCIDENT-WINDOW` with expiry.
5. Risk `RK-STR005-INCIDENT-DRIFT`.
6. Validate: seats present; expiry enforced.

## 3. Delay (`delay.json`)

1. Trigger: pilot milestone slip.
2. Decision: owner **program**, reviewer **sponsor**, board Program.
3. Rejected proposal: seats = engineering only for RescheduleGoLive → **FAIL**.
4. Accepted: program + sponsor for RescheduleMilestone → PASS.
5. Risk `RK-STR005-DELAY-SILENT` → Mitigated.
6. Validate harness must treat eng-only Go-Live evidence as failure (see go-live fixtures).

## Closure of the simulation exceptions (PLAN-34, D-QA-3)

The simulation is finished. Both seed exceptions in `docs/str-governance/governance-matrix.json` are `Closed` on
2026-10-30 by decision `PG-EXC-2026-10-001` (`docs/str-governance/decision-log.json`); no expiry date was extended.
`EX-STR005-SIM-INCIDENT-WINDOW` had expired on 2026-09-15 and blocked the STR-005 CI step and the Production/Staging
startup of Gateway, HR.Core and Payroll; `EX-STR005-SIM-LAW-INTERIM` expired at the end of 2026-10-30.

The walkthroughs and fixtures above remain valid as scenarios. Expiry enforcement is still proven, on a copy of the
matrix and a fixed clock, by `Str005ProductionBindingTests` and `RuntimeGovernanceDocumentsTests` (AC-QA-34c).

Renewing a real exception requires a new decision row, a `Renewed` status with a different reviewer, and either an image
rebuild (the matrix is copied to the build output) or `ProgramGovernance:MatrixPath` pointing at a mounted file. Every
`Declared` exception is a dated obligation in `docs/qa/dated-obligations.json` (warn 30 days, fail 7 days before expiry).

## Cross-check

| Scenario | decision-log | risk-register | exception |
|---|---|---|---|
| Law | PG-LAW-2026-07-SIM | RK-STR005-LAW-LAG | EX-STR005-SIM-LAW-INTERIM |
| Incident | PG-INC-2026-07-SIM | RK-STR005-INCIDENT-DRIFT | EX-STR005-SIM-INCIDENT-WINDOW |
| Delay | PG-DELAY-2026-07-SIM | RK-STR005-DELAY-SILENT | — |
