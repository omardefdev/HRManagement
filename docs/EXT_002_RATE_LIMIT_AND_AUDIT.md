# EXT-002 — Rate limit and audit

| Control | Value |
|---|---|
| maxQueriesPerMinute | 30 (lab) |
| bulkHarvestForbidden | true |
| audit.required | true |

Audit fields: `queryId`, `purposeCode`, `actor`, `tenantId`, `outcome`, `queriedAt`.

Bulk enumeration / harvest attempts → FAIL (`bulk-harvest-fail`). Every query audited → PASS (`query-audit-pass`).
