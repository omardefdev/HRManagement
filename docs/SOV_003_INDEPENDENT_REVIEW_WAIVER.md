# SOV-003 — Independent review waiver (production key-sovereignty binding)

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | SOV-003 |
| Date | 2026-07-30 |
| Expires | When SOV-003a–e live stories are Accepted, or 2027-01-31 — whichever first |

## Waiver statement

Live HSM/KMS (SOV-003a), key ceremony workers (SOV-003b), live rotation/failover (SOV-003c), and trusted time (SOV-003d) are **deferred**. SOV-004 platform zero-trust binding is Satisfied separately (live PAM remains deferred). Engineering adopts lab key-sovereignty contracts as a startup/CI binding. This waiver does **not** authorize live HSM, closing SEC-004, flipping EXT-004 `hsm-prod` to Satisfied, or closing SIG-001 / NAT-001.

## Conditions

1. Matrix soft blockers remain honest: SEC-004 / live-hsm-prod / live-key-ceremony-workers / live-trusted-time=`Blocked`; SOV-004=`Satisfied` (zero-trust binding); SIG-001/NAT-001=`NotStarted`.
2. `SovKeys:EnforceOnStartup` remains true in Staging/Production.
3. Default `LiveKeyCustodyClaim=false`.
4. EXT-004 `hsm-prod` remains Blocked.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Cryptography Authority | Matrix + fixtures acceptable for interim |
| CISO | Vendor-alone / dual-control / rotation flags accepted |
| Product | Live HSM remains SOV-003a / EXT-004b |
