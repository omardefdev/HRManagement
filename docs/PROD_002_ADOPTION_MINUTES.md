# PROD-002 — Program adoption minutes (agency-selfadmin production binding)

| Field | Value |
|---|---|
| Card | PROD-002 (card 81) |
| Date | 2026-07-30 |
| Decision | Adopt lab agency-selfadmin as startup/CI binding; LiveAgencySelfAdminClaim=false |

## Claim gates

| Claim | Requires |
|---|---|
| `LiveAgencySelfAdminClaim=true` | PROD-002a + live-agency-selfadmin Satisfied |
| Live delegated structure/roles | PROD-002b + live-delegated-admin-ui |
| Live quotas/calendars/notifications | PROD-002c |
| ONB-001 join | PROD-002d |
| PROD-003 join | PROD-002e |

## Cross-card

PROD-001 policy matrix and MIG-007 onboarding matrix blocker PROD-002 flipped to Satisfied with this binding.
