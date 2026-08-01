# SOV-004 Independent Review Waiver — Production zero-trust binding

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-004 |
| Scope | Lab/contract zero-trust binding only |

## Waiver

Live PAM/JIT workers (SOV-004a), admin workstation enforcement (SOV-004b), SOC break-glass (SOV-004c), and access recertification workers (SOV-004d) are **deferred**. SOV-005 platform threat-modeling binding is Satisfied separately (live TM review remains deferred). Engineering adopts lab zero-trust contracts as a startup/CI binding. This waiver does **not** authorize `LivePamClaim=true`, raise `LiveKeyCustodyClaim`, or close NAT-001.

## Conditions

1. Matrix soft blockers remain honest: live-pam-workers / live-admin-workstation-enforcement / live-break-glass-soc / live-access-recertification=`Blocked`; SOV-005=`Satisfied` (threat-modeling binding); NAT-001=`NotStarted`.
2. Staging/Production keep `EnforceOnStartup=true` and `LivePamClaim=false`.
3. SOV-003 accepts SOV-004=`Satisfied` as platform binding only.
