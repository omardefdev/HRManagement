# SOV-001 — Independent review waiver (production control-baseline binding)

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | SOV-001 |
| Date | 2026-07-30 |
| Expires | When SOV-001a–e live stories are Accepted, or 2027-01-31 — whichever first |

## Waiver statement

Live independent ATO (SOV-001a), continuous assessment workers (SOV-001b), live POA&M system (SOV-001c), SYR-001 corpus join (SOV-001d), and SOV-002 expansion (SOV-001e) are **deferred**. Engineering adopts lab control-baseline contracts as a startup/CI binding. This waiver does **not** authorize live ATO, flipping STR-002 `Blocked-Legal` controls to Implemented, or closing SYR-001 / SOV-002 / NAT-001.

## Conditions

1. Matrix soft blockers remain honest: live-independent-ato / continuous-assessment-workers / live-poam-system=`Blocked`; SYR-001/NAT-001=`NotStarted`; SOV-002=`Satisfied` (data-sovereignty binding; live egress = SOV-002a).
2. `SovBaseline:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveAtoClaim=false`.
4. STR-002 `CTRL-*` Blocked-Legal entries remain Blocked-Legal.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| CISO | Matrix + fixtures acceptable for interim |
| Compliance | Coverage/ownership/POA&M flags accepted |
| Product | Live ATO remains SOV-001a |
