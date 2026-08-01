# MIG-003 — Staging zones

| zone | encryptionAtRest | accessRole | tenantScoped | purge |
|---|---|---|---|---|
| mig-raw | Yes | migration-extract | Yes | 7d or wave complete |
| mig-valid | Yes | migration-transform | Yes | 7d |
| mig-mapped | Yes | migration-load | Yes | 7d |
| mig-quarantine | Yes | migration-qa | Yes | 30d or resolved |

Zones are **contracts** for OPS-005 / future MIG-003a I/O. App REL-001 staging overlays are not migration extract zones. Every zone must be encrypted at rest and tenant-scoped; unencrypted zone fails validate.
