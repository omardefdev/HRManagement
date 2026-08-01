# SOV-004 Adoption Minutes — Production zero-trust binding

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | SOV-004 (90) |
| Decision | Adopt production zero-trust binding with `LivePamClaim=false` |

## Claim gates (deferred)

| Claim / capability | Requires |
|---|---|
| `LivePamClaim=true` | SOV-004a + live-pam-workers Satisfied |
| Live admin workstation enforcement | SOV-004b + live-admin-workstation-enforcement |
| Live SOC break-glass | SOV-004c + live-break-glass-soc |
| Live access recertification | SOV-004d + live-access-recertification |
| SOV-005 threat modeling join | Satisfied (production threat-modeling binding; live TM review = SOV-005a) |

## Cross-card

SOV-003 blocker `SOV-004` is Satisfied (platform zero-trust binding). SOV-005 is Satisfied as production threat-modeling binding (not live TM review). This does **not** authorize live PAM/JIT, raise `LiveKeyCustodyClaim`, or close NAT-001.
