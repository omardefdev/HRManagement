# EXT-004 — LTV and archive evidence

`ltv.requiredAfterExpiry` = true. After signing-cert expiry, validation must use LTV materials (timestamp + archived cert chain + evidence package).

| Scenario | Outcome |
|---|---|
| Expired cert, no LTV package, treated valid | FAIL (`expired-without-ltv-fail`) |
| Expired cert, LTV + TSA present | PASS (`expired-cert-ltv-pass`) |
| Archive restore then re-verify | PASS (`archive-restore-verify-pass`) |

`archiveEvidenceRequired` joins OBS-003 hash/WORM evidence — this pack defines the contract; live archive product bind is later.
