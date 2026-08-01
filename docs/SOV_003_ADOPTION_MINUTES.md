# SOV-003 — Adoption minutes (production key-sovereignty binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-003 |
| Decision | Adopt lab key-sovereignty matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveKeyCustodyClaim=true` | SOV-003a + live-hsm-prod Satisfied + SEC-004 Satisfied |
| Live key ceremony workers | SOV-003b + live-key-ceremony-workers |
| Live rotation/failover | SOV-003c |
| Live trusted time | SOV-003d + live-trusted-time |
| SOV-004 Zero Trust join | Satisfied (production zero-trust binding; live PAM = SOV-004a) |

## Cross-card sync

- SOV-002 `data-sovereignty-matrix.json`: SOV-003 → Satisfied
- EXT-004 `pki-signature-matrix.json`: SOV-003 → Satisfied; `hsm-prod` remains Blocked

## Notes

SEC-004 remains Blocked. SOV-004 is Satisfied as production zero-trust binding (not live PAM). SIG-001 and NAT-001 remain NotStarted. This binding does not authorize live HSM or flip `hsm-prod`.
