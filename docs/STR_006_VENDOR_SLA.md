# STR-006 — Vendor SLA clauses

Required clauses: **RTO**, **IncidentDisclosure**, **Exit**. Envelope RTO target: 240 minutes (4h) from STR-001 — vendor `rtoHours` must be ≤ 4 for critical components in this stub.

| vendorId | components | rtoHours | disclosureHours | exit | clauses |
|---|---|---|---|---|---|
| v-db | postgres | 4 | 24 | Yes | RTO, IncidentDisclosure, Exit |
| v-cloud-host | gateway, hr-core | 4 | 12 | Yes | RTO, IncidentDisclosure, Exit |
| v-idp-support | auth-directory | 4 | 24 | Yes | RTO, IncidentDisclosure, Exit |

FAIL fixture: `testdata/str006/vendors/missing-exit-clause.json`.

Vendor-exit production binding = **SOV-007** Done; live clean-room / exit drill = **VEN-001**. Operational vendor support contracts = **SUP-004**.
