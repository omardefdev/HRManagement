# SOV-006 Independent Review Waiver — Production pen-red binding

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-006 |
| Scope | Lab/contract pen-red binding only |

## Waiver

Live independent pen assessment (SOV-006a), red team exercise (SOV-006b), purple detection validation (SOV-006c), remediation/retest closure (SOV-006d), and NAT-002 join (SOV-006e) are **deferred**. Engineering adopts lab pen-red contracts as a startup/CI binding. This waiver does **not** authorize `LivePenClaim=true`, raise `LiveThreatModelClaim`, or close NAT-001 / NAT-002 / FRD-001. (SOV-007 is a separate production vendor-exit binding Done card.)

## Conditions

1. Matrix soft blockers remain honest: live-independent-pen-assessment / live-red-team-exercise / live-purple-detection-validation / live-remediation-retest-closure=`Blocked`; NAT-001/NAT-002=`NotStarted`.
2. Staging/Production keep `EnforceOnStartup=true` and `LivePenClaim=false`.
3. SOV-005 accepts SOV-006=`Satisfied` as platform binding only.
