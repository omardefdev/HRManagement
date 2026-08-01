# PROD-002 — Journey matrix

All steps mandatory. Evidence via matrix flags + `testdata/prod002` fixtures.

| Journey | Evidence |
|---|---|
| DailyDelegatedChange | daily-structure-change-pass / dba-required-path-fail |
| NationalAndTenantCeiling | national-control-mutate-fail / cross-tenant-admin-fail |
| Accountability | no-rollback-fail / missing-audit-fail / rollback-pass |
| SensitiveFourEyes | same-role-sensitive-fail / sensitive-four-eyes-pass |
| HostileConcurrent | hostile-quota-bypass-fail / role-template-preview-pass / concurrent-change-pass |
