# EXT-005 — Journey matrix

Source: [`ext-api/api-exchange-matrix.json`](ext-api/api-exchange-matrix.json) — [ADR-EXT-005](ADR-EXT-005-gov-data-exchange-api-contracts.md).

## CatalogPublish

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CatalogPolicy | Yes | architecture | catalogRequired |

## ContractOwnerSla

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| OwnerSlaPolicy | Yes | api-gov | requiredCatalogFields |
| MissingOwnerSlaFail | Yes | qa | missing-owner-sla-fail |

## BackwardCompatMeasured

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CompatPolicy | Yes | api-gov | compatibility |
| BreakingWithoutBumpFail | Yes | qa | breaking-without-bump-fail |
| N1CompatPass | Yes | qa | n1-compat-pass |

## NoSharedDb

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| SharedDbPolicy | Yes | architecture | sharedDbForbidden |
| SharedDbFail | Yes | qa | shared-db-fail |

## PurposeBoundExport

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PurposePolicy | Yes | privacy | purposeBoundExport |
| PurposeDeniedExportFail | Yes | qa | purpose-denied-export-fail |

## ConsumerDrivenBreak

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CdcPolicy | Yes | api-gov | consumerDriven |
| CertifiedConsumerBreakFail | Yes | qa | certified-consumer-break-fail |

## SchemaRegistryCompat

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RegistryPolicy | Yes | integration | schemaRegistry |
| SchemaIncompatFail | Yes | qa | schema-incompat-fail |

## ThrottleAndCorrelation

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ThrottlePolicy | Yes | security | throttling |
| ThrottleExceedPass | Yes | qa | throttle-exceed-pass |

## DeprecationWindow

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DeprecationPolicy | Yes | api-gov | deprecation |
| DeprecationTooShortFail | Yes | qa | deprecation-too-short-fail |

## InteropHarness

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| InteropPass | Yes | qa | interop-harness-pass |
| ExchangeAuditPass | Yes | qa | exchange-audit-pass |

Soft blockers: PROD-007 = Satisfied (extensibility-platform binding); gov-exchange-bus-live / schema-registry-live / partner-cdc-cert / national-openapi-publish = Blocked.
