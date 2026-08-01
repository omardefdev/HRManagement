# EXT-002 — Minimal query

## Allowed request fields

`nationalId`, `purposeCode`, `consentRef`, `requesterTenantId`

## Allowed response fields

`matchStatus`, `opaqueSubject`, `assuranceHint`, `queriedAt`

Storing or persisting response fields outside the allowlist → FAIL (`excess-field-store-fail`). Prefer opaque subjects; no bulk dumps of civil attributes.
