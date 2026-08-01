# EXT-004 — Trusted timestamp

`trustedTimestamp.requiredForLtv` = true. Long-term validation requires a trusted timestamp token (lab mock TSA).

## Outage

`tsaOutageDegrade` = `hold-not-valid`. On TSA outage:

- Do not mark signature as LTV-complete
- Hold pending timestamp; do not treat as legally settled

Fixture: `tsa-outage-degrade-pass` (held, not treated as valid). Live TSA = `tsa-live` Blocked.
