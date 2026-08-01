# STR-002 — Independent legal/ATO review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | STR-002 |
| Expires | When SYR-001 is Accepted and live SOV-001 ATO (SOV-001a) is Accepted, or 2027-01-31 — whichever first |
| Owner | Legal + Program Board + CISO |

## Waiver statement

An external independent legal corpus certification and NIST-style ATO crosswalk are **deferred**. Engineering adopts the control catalog as an operational release gate under Interim profile. This waiver does **not** authorize:

- Claiming Production `NationalClaim` while critical `Blocked-Legal` remain
- Treating interim tax/privacy/signature rows as certified national law
- Skipping SYR-001 gazette corpus or live SOV-001 ATO (SOV-001 production control-baseline binding Done does not authorize live ATO)
- Shipping `LEGAL_TBD` on Implemented controls

## Conditions

1. `LegalControl:AllowBlockedLegalNationalClaim` remains false in Production.
2. `LegislativeWatch` stamp is refreshed before `nextDue`.
3. SYR-001 / SYD OPEN-NO-GO samples remain soft blockers; SOV-001 catalog blocker is Satisfied (baseline binding; live ATO = SOV-001a).

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Legal | Engineering waiver accepted pending SYR-001 |
| Program Board | Interim profile binding implemented |
| CISO | Monitoring gauges `HRManagement.LegalControl` |
