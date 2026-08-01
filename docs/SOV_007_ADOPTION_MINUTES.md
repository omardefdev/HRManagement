# SOV-007 Adoption Minutes

| Field | Value |
|---|---|
| Card | SOV-007 |
| Decision | Adopt production vendor-exit binding |
| Date | 2026-07-30 |

## Claim gates

| Claim | Requires |
|---|---|
| `LiveVendorClaim=true` | SOV-007a + live-clean-room-rebuild Satisfied |
| Live vendor-exit drill | SOV-007b + live-vendor-exit-drill (VEN-001 path) |
| Live full export/import | SOV-007c + live-full-export-import |
| Live destruction attestation | SOV-007d + live-vendor-destruction-attestation |
| Join VEN-001 / NAT-002 | SOV-007e |

STR-006 blocker `SOV-007` is Satisfied (platform vendor-exit binding). This does **not** authorize live exit drill, raise `National24x7Claim`, or close VEN-001 / NAT-001 / SAN-001.
