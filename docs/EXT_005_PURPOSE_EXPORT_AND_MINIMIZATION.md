# EXT-005 — Purpose-bound export and minimization

## Purpose

`purposeBoundExport.consentRequired` = true. Exports must use an allowlisted purpose:

- payroll-disbursement-export
- civil-registry-proof-query
- agency-onboarding-sync
- audit-evidence-export

Purpose outside allowlist → FAIL (`purpose-denied-export-fail`).

## Minimization

`dataMinimization.forbidExcessFields` = true. Excess PII fields in export payloads fail readiness for production bind (lab fixtures assert purpose deny first).

Shared-database joins as an “integration” are forbidden (`sharedDbForbidden`) — FAIL (`shared-db-fail`).
