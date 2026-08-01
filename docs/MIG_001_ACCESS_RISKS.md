# MIG-001 — Access risks

| sourceId | risks | severity notes |
|---|---|---|
| legacy-hr-batch | shared-dba-account, no-row-level-audit, pii-in-extract | High — need dedicated read role |
| legacy-payroll | vendor-vpn-only, credential-rotation-unknown, pii-in-extract | High |
| legacy-org-files | unencrypted-share, broad-acl | Medium |
| legacy-docs-share | no-dlp, pii-attachments, virus-scan-gap | High |
| civil-api-stub | api-key-shared, no-staging-sandbox | Medium |
| paper-personnel-files | physical-theft, incomplete-index, no-digital-backup | High |
| legacy-access-control-unsupported | vendor-lock, no-export-api | Critical — ExceptionSigned |

Inventoried sources must have non-empty `accessRisks[]`. Undocumented risk fixture → FAIL.
