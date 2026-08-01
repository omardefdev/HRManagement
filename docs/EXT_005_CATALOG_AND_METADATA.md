# EXT-005 — Catalog and metadata

Every published government integration must appear in the lab catalog with:

| Field | Rule |
|---|---|
| integrationId | Stable unique id |
| contractRef | Pointer to OpenAPI/event schema version |
| owner | Accountable team/role |
| sla | Availability / latency commitment |

`catalogRequired` = true. Missing owner or SLA → FAIL (`missing-owner-sla-fail`).

Dev Swagger via `HrOpenApiExtensions` is **not** governed publish (`national-openapi-publish` Blocked). Live catalog publish = EXT-005a.

See [ADR-EXT-005](ADR-EXT-005-gov-data-exchange-api-contracts.md).
