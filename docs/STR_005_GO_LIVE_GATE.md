# STR-005 — Go-Live gate

**Rule:** Go-Live (pilot production cutover or national wave) cannot be declared by engineering alone.

## Required seats

From `goLiveMultiSeat` in [`str-governance/governance-matrix.json`](str-governance/governance-matrix.json):

| Seat | Role placeholder | Required |
|---|---|---|
| Sponsor | sponsor | Yes |
| Security | security | Yes |
| Architecture | architecture | Yes |
| Data | data | When data-model / migration impact |

Forbidden: `eng-only` (aliases: engineering, eng, developer, dev as sole seats).

## Fixtures

| File | expectedValidate |
|---|---|
| `testdata/str005/go-live/eng-only.json` | **FAIL** |
| `testdata/str005/go-live/multi-seat.json` | **PASS** |

## Soft blocker

**SYD-017** — human names for these seats remain OPEN in file 14. Until then, role-based evidence is accepted; claiming named sign-off without SYD-017 is an exception-path violation.

## Related

- REL-002 pilot Go/No-Go checklist (consumes multi-seat spirit)
- Policy decision: `PG-GOLIVE-GATE-POLICY` in decision-log
