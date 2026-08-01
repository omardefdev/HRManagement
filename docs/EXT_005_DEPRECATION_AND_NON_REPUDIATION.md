# EXT-005 — Deprecation and non-repudiation

## Deprecation

`deprecation.noticeDaysMin` = 90. Sunset of a published contract requires a major version (`sunsetRequiresMajor`). Notice shorter than minimum → FAIL (`deprecation-too-short-fail`).

## Non-repudiation

`auditRequired` = true. Every exchange audit entry must include correlationId and outcome (see `exchange-audit-pass`).
