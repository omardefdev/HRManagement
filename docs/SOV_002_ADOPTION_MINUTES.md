# SOV-002 — Adoption minutes (production data-sovereignty binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-002 |
| Decision | Adopt lab data-sovereignty matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveSovereigntyClaim=true` | SOV-002a + live-egress-enforcement Satisfied |
| JIT support workers | SOV-002b + live-support-jit-workers |
| Live deletion certificate system | SOV-002c + live-deletion-certificate-system |
| SYR-008 hosting authorization | SOV-002d |
| SOV-003 key sovereignty expansion | SOV-002e |

## Cross-card sync

- SOV-001 `control-baseline-matrix.json`: SOV-002 → Satisfied
- `LiveAtoClaim` remains false; CTRL-* Blocked-Legal unchanged

## Notes

SYR-008 and NAT-001 remain NotStarted. SOV-003 catalog blocker is Satisfied (production key-sovereignty binding; live HSM = SOV-003a). This binding does not authorize live egress, JIT support, or deletion systems.
