# STR-004 — Upgrade rehearsal stub

STR-004 does **not** implement versioned override inheritance. Full proof that upgrades preserve agency customization is **Blocked on PROD-001**.

## Status

| Field | Value |
|---|---|
| blockedOn | PROD-001 |
| status | Blocked |
| acceptanceLink | PROD-001 when Done |

## Checklist (consumed from PROD-001)

| Item | Done? | Evidence |
|---|---|---|
| Upgrade from digest N to N+1 | | |
| Agency A/B entitlements unchanged unless pack bumped | | |
| Branding preserved | | |
| Policy/workflow overrides preserved | | |
| Rollback of config pack without binary fork | | |

## Until then

Dual-agency fixtures + promotion schema demonstrate the **policy**; runtime preservation waits for PROD-001.
