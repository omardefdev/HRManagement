# SOV-005 Independent Review Waiver — Production threat-modeling binding

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-005 |
| Scope | Lab/contract threat-modeling binding only |

## Waiver

Live independent threat-model review (SOV-005a), attack-tree walkthrough (SOV-005b), misuse backlog sync (SOV-005c), and residual risk signoff (SOV-005d) are **deferred**. SOV-006 platform pen-red binding is Satisfied separately (live pen remains deferred). Engineering adopts lab threat-model contracts as a startup/CI binding. This waiver does **not** authorize `LiveThreatModelClaim=true`, raise `LivePamClaim`, or close NAT-001 / FRD-001.

## Conditions

1. Matrix soft blockers remain honest: live-independent-tm-review / live-attack-tree-walkthrough / live-misuse-backlog-sync / live-residual-risk-signoff=`Blocked`; SOV-006=`Satisfied` (pen-red binding); NAT-001=`NotStarted`.
2. Staging/Production keep `EnforceOnStartup=true` and `LiveThreatModelClaim=false`.
3. SOV-004 accepts SOV-005=`Satisfied` as platform binding only.
