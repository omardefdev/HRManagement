# MIG-007 — Readiness assessment

Mandatory gates before an agency may enter a wave:

| Gate | Meaning |
|---|---|
| tenant | Tenant provisioned from standard release (no fork) |
| config | Config promoted per STR-004 taxonomy |
| roles | Baseline roles assigned |
| integrations | Declared integrations certified (lab stub → EXT-006) |
| data | MIG readiness / extract plan acknowledged |
| training | Training complete for key personas |
| support | Support contact + escalation registered |

All gates must be `ok` in the checklist fixture. Missing any gate fails validate.
