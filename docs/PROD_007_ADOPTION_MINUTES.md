# PROD-007 — Adoption minutes (production extensibility-platform binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | PROD-007 |
| Decision | Adopt lab extensibility matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveExtensibilityClaim=true` | PROD-007a + live-extension-runtime Satisfied |
| Partner SDK حي | PROD-007b + live-partner-sdk |
| Webhook delivery حي | PROD-007c + live-webhook-delivery |
| EXT-006 certification surface | PROD-007d |
| REL-004 / upgrade ops حي | PROD-007e |

## Cross-card sync

- EXT-005 api-exchange matrix: PROD-007 → Satisfied
- PROD-006 semantic-layer matrix: PROD-007 → Satisfied (`live-open-data-feed` remains Blocked)

## Notes

EXT-006, IOP-001, and REL-004 remain NotStarted. This binding does not authorize live extension runtime or partner SDKs.
