# EXT-001 — Attribute authority

## Claims allowlist

`sub`, `tenant_id`, `org_scope`, `aal`, `fal`, `persona`, `opaque_subject`.

## Linking

`noNameOnlyLinking: true` — account linking by `FullName` / `display_name` alone is forbidden (`name-only-link-fail`).

Prefer opaque subjects; plain NationalId must not appear in federation logs (`forbidPlainNationalIdInLogs`).
