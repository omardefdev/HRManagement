# MIG-001 — Freeze constraints

| sourceId | constraints |
|---|---|
| legacy-hr-batch | extract-freeze-T-7d; no-schema-change-during-wave |
| legacy-payroll | extract-freeze-T-3d; period-close-before-extract |
| legacy-org-files | freeze-folder-T-7d |
| legacy-docs-share | copy-then-freeze; checksum-before-cutover |
| civil-api-stub | no-bulk-export-without-MOU |
| paper-personnel-files | scan-batch-freeze; chain-of-custody |
| legacy-access-control-unsupported | manual-defer-to-wave-2 |

Cutover rehearsals (MIG-006) consume these windows; this pack only declares them.
