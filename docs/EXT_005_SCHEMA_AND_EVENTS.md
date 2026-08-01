# EXT-005 — Schema and events

Lab schema registry is allowed (`schemaRegistry.labMockAllowed`); live registry forbidden (`liveForbidden`).

API and event schemas must register a version compatible with N-1 consumers. Incompatible schema treated as published → FAIL (`schema-incompat-fail`).

Joins internal [`MESSAGE_EVENT_MATRIX.md`](MESSAGE_EVENT_MATRIX.md) / `EventSchemaVersion` as documentation only — not a production registry.
