# PROD-005 — Adoption minutes (production workbox binding)

| Field | Value |
|---|---|
| Date | 2026-07-30 |
| Card | PROD-005 |
| Decision | Adopt lab workbox matrix as startup/CI production binding |

## Claims and promotion gates

| Claim | Requires |
|---|---|
| `LiveWorkboxClaim=true` | PROD-005a + live-workbox-ui Satisfied + IAM-002 Satisfied |
| Live notification delivery | PROD-005b + live-notification-delivery |
| Live safe search | PROD-005c + live-safe-search |
| COM-001 / WF-002 | PROD-005d |
| NAT-005 | PROD-005e |
| PROD-006 (semantic-layer binding) | Satisfied via PROD-006 Done — live BI remains PROD-006c |

## Cross-card sync

- PROD-004 remote-branch matrix: PROD-005 → Satisfied
- EXT-007 sector-catalog matrix: PROD-005 → Satisfied

## Notes

IAM-002 and COM-001 remain NotStarted. NotificationHub broadcast-to-all without receipts remains a COM-001 gap, not closed by this binding.
