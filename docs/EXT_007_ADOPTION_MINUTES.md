# EXT-007 — Program adoption minutes (sector-catalog production binding)

| Field | Value |
|---|---|
| Card | EXT-007 (card 79) |
| Date | 2026-07-30 |
| Decision | Adopt lab sector-catalog as startup/CI binding; LiveSectorCatalogClaim=false |

## Claim gates

| Claim | Requires |
|---|---|
| `LiveSectorCatalogClaim=true` | EXT-007a + live-sector-adapters Satisfied |
| Live notification channel | EXT-007b + live-notification-channel |
| Adapter fleet certified live | EXT-007c + adapter-fleet-certified-live |
| PROD-005 join | EXT-007d |
| COM-001 join | EXT-007e |

## Cross-card

EXT-006 sandbox matrix blocker EXT-007 flipped to Satisfied with this binding.
