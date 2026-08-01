# STR-001 — Independent capacity review waiver

| Field | Value |
|---|---|
| Status | Active waiver |
| Card | STR-001 |
| Expires | When CAP-001 national load proof is Accepted, or 2027-01-31 — whichever first |
| Owner | Program Board + SRE |

## Waiver statement

An external independent capacity consultant review is **deferred**. Engineering adopts syria-interim envelope numbers as operational ceilings. This waiver does **not** authorize:

- Claiming Production `national-competition` scale
- Skipping CAP-001 soak/load proof
- Treating lab `scale_factor` as national proof

## Conditions

1. `NationalScope:AllowNationalCompetitionClaim` remains false in Production.
2. CAP-001 remains Blocked on STR-001 backlog until executed.
3. SYR-002 may raise syria-interim persons indicators; lowering requires signed EXCEPTION.

## Sign-off slots

| Role | Acknowledgement |
|---|---|
| Program Board | Engineering waiver accepted pending CAP-001 |
| Architecture | Envelope binding implemented |
| SRE | Monitoring gauges `HRManagement.NationalScope` |
