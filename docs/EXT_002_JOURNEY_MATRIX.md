# EXT-002 — Journey matrix

Source: [`ext-civil/civil-registry-matrix.json`](ext-civil/civil-registry-matrix.json) — [ADR-EXT-002](ADR-EXT-002-civil-registry-person-proofing.md).

## PurposeConsent

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PurposeAllowlist | Yes | privacy | purposeAllowlist |
| PurposeMismatchFail | Yes | qa | purpose-mismatch-fail |

## MinimalData

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FieldAllowlist | Yes | integration | minimalQuery |
| ExcessStoreFail | Yes | qa | excess-field-store-fail |

## MismatchHuman

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| WorkflowPolicy | Yes | integration | mismatchHumanWorkflow |
| MismatchReviewPass | Yes | qa | mismatch-review-pass |

## UnavailableMode

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DegradePolicy | Yes | integration | unavailableMode |
| OutageAsTruthFail | Yes | qa | outage-as-truth-fail |
| UnavailableDegradePass | Yes | qa | unavailable-degrade-pass |

## RateLimitAbuse

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RatePolicy | Yes | security | rateLimits |
| BulkHarvestFail | Yes | qa | bulk-harvest-fail |

## AuditTrail

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AuditPolicy | Yes | privacy | audit |
| QueryAuditPass | Yes | qa | query-audit-pass |

## StaleCorrection

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CachePolicy | Yes | integration | caching |
| StaleFail | Yes | qa | stale-as-truth-fail |
| CorrectionPass | Yes | qa | correction-workflow-pass |

Soft blockers: moi-civil-registry-live, live-sandbox-cert, national-idp-live = Blocked; L10N-002 = NotStarted.
