# STR-004 — Configuration promotion and rollback

## Signed config pack (schema)

Required fields (also in nofork-matrix `signedConfigPackSchema`):

| Field | Meaning |
|---|---|
| packId | Unique pack id |
| agencyId | Target agency |
| releaseDigest | Product release this pack applies to |
| reviewerRoles | e.g. product, security, agency-sponsor |
| rollbackRef | Prior pack id or digest to restore |
| payload | Entitlements, branding, future policy refs |

## Process (baseline)

1. Author pack for agency (from dual-agency fixtures pattern).
2. Peer review (roles listed) — checklist sign-off.
3. Promote with same product digest as staging/prod (DEP-001).
4. Rollback = re-apply `rollbackRef` pack; do not redeploy a forked binary.

## Blocked (do not claim Done here)

| Capability | blockedOn |
|---|---|
| Cryptographic signature / four-eyes publish engine | PROD-001 / ONB-001 |
| Versioned policy/workflow payload validation | PROD-001 |
