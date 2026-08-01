# MIG-001 — Source catalog

Machine source: [`mig-inventory/source-inventory.json`](mig-inventory/source-inventory.json).

| sourceId | type | agency | status | rows | files | estimateOnly |
|---|---|---|---|---:|---:|---|
| legacy-hr-batch | database | agency-pilot-a | Inventoried | 125000 | 0 | No |
| legacy-payroll | database | agency-pilot-a | Inventoried | 980000 | 0 | No |
| legacy-org-files | file | agency-pilot-a | Inventoried | 4200 | 18 | No |
| legacy-docs-share | file | agency-pilot-a | Inventoried | 0 | 54000 | No |
| civil-api-stub | api | national-civil | Inventoried | 0 | 0 | No |
| paper-personnel-files | paper | agency-pilot-a | Inventoried | 0 | 8200 | No |
| legacy-access-control-unsupported | unsupported | agency-pilot-b | Unsupported | — | — | Yes (exception) |

Universe coverage: every `knownSourceUniverse` id appears with Inventoried, ExceptionSigned, or Unsupported + signed exception.
