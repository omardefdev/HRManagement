# SOV-001 — Adoption minutes (production control-baseline binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-001 |
| Decision | Adopt lab control-baseline matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveAtoClaim=true` | SOV-001a + live-independent-ato Satisfied |
| Continuous assessment workers | SOV-001b + continuous-assessment-workers |
| Live POA&M system | SOV-001c + live-poam-system |
| SYR-001 corpus join | SOV-001d |
| SOV-002 data sovereignty expansion | SOV-001e |

## Cross-card sync

- STR-002 `control-catalog.json`: SOV-001 → Satisfied
- `CTRL-*` with `Blocked-Legal` remain Blocked-Legal (not Implemented)

## Notes

SYR-001 and NAT-001 remain NotStarted. SOV-002 catalog blocker is Satisfied (production data-sovereignty binding; live egress = SOV-002a). This binding does not authorize live independent ATO or NationalClaim without STR-002 rules.
