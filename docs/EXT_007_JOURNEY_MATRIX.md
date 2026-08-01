# EXT-007 — Journey matrix

All steps mandatory. Evidence via matrix flags + `testdata/ext007` fixtures.

| Journey | Evidence |
|---|---|
| NoImplicitCritical | implicit-critical-fail / catalog-entry-pass |
| AdapterCompleteness | missing-contract/slo/exit-fail |
| NotificationPii | notify-pii-fail |
| FailureSurfacing | swallowed-failure-fail / outage-visible-pass |
| CertReconReceipts | uncertified-adapter-enable-fail / reconciliation-pass / delivery-receipt-pass / adapter-cert-audit-pass |
