# SOV-002 — Independent review waiver (production data-sovereignty binding)

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | SOV-002 |
| Date | 2026-07-30 |
| Expires | When SOV-002a–e live stories are Accepted, or 2027-01-31 — whichever first |

## Waiver statement

Live egress enforcement (SOV-002a), JIT support workers (SOV-002b), live deletion certificate system (SOV-002c), SYR-008 hosting join (SOV-002d), and SOV-003 expansion (SOV-002e) are **deferred**. Engineering adopts lab data-sovereignty contracts as a startup/CI binding. This waiver does **not** authorize live egress, JIT support, deletion systems, or closing SYR-008 / SOV-003 / NAT-001.

## Conditions

1. Matrix soft blockers remain honest: live-egress-enforcement / live-support-jit-workers / live-deletion-certificate-system=`Blocked`; SYR-008/NAT-001=`NotStarted`; SOV-003=`Satisfied` (key-sovereignty binding; live HSM = SOV-003a).
2. `SovData:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveSovereigntyClaim=false`.
4. `LiveAtoClaim` and STR-002 Blocked-Legal entries remain unchanged.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Data Sovereignty | Matrix + fixtures acceptable for interim |
| Legal / Privacy | Inventory/residency/egress flags accepted |
| Product | Live egress remains SOV-002a |
