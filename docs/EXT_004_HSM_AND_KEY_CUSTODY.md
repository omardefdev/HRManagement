# EXT-004 — HSM and key custody

`hsmCustody.privateKeyNeverLeavesHsm` = true. `labMockAllowed` = true for this pack.

| Forbidden | Result |
|---|---|
| Export private key material from HSM/mock HSM | FAIL (`key-exfil-fail`) |
| Soft-key file used as production custody claim | EXCEPTION (claiming hsm-prod Done) |

Production HSM bind = EXT-004b / SOV-003a. SOV-003 platform key-sovereignty binding is Satisfied; soft blocker `hsm-prod` remains Blocked (mock HSM only).
