# MIG-004 — Review privacy

| Control | Required |
|---|---|
| tenantScopedReview | true |
| forbidCrossAgencyPii | true |
| maskingMode | hash-or-omit |

A reviewer authorized for tenant A must not receive plaintext PII from tenant B in match/review payloads. Cross-tenant review fixture must FAIL validate.

Aligns with GOV-001 classification and TEN-002 isolation; this pack proves the migration-match review contract only.
