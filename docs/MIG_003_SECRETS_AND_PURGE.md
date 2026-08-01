# MIG-003 — Secrets and purge

| Topic | Rule |
|---|---|
| secrets store | OPS-005 secret manager contract |
| rotateBeforeWave | true |
| noSecretsInLogs | true |
| staging purge | per zone purgePolicy (7d / 30d quarantine) |
| purge stage | pipeline stage `purge` after successful load |

Secure staging review checklist must confirm encryption, access roles, purge, and secret rotation refs. Live secret provisioning remains OPS-005 / MIG-003a.
