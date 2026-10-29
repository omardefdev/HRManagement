# PRV-001 — سجل أنشطة المعالجة (ROPA)

Machine-readable source: [`prv-ropa/processing-activities.json`](./prv-ropa/processing-activities.json).  
Builds on [`DATA_CLASSIFICATION_INVENTORY.md`](./DATA_CLASSIFICATION_INVENTORY.md) and retention matrix.

## Controller / processors

| Role | Party |
|---|---|
| Controller | National HR Platform Operator (ministry/agency program) |
| Processors (infra) | PostgreSQL host, MinIO, Keycloak, observability stack |
| Subprocessors | See [PRV_001_SUBPROCESSOR_REGISTER.md](./PRV_001_SUBPROCESSOR_REGISTER.md) |

## Coverage

All 14 services have at least one processing activity in the JSON register. High-risk activities require a DPIA:

| Activity | DPIA |
|---|---|
| Employment / National ID | [PRV_001_DPIA_IDENTITY.md](./PRV_001_DPIA_IDENTITY.md) |
| Payroll | [PRV_001_DPIA_PAYROLL.md](./PRV_001_DPIA_PAYROLL.md) |
| Health | [PRV_001_DPIA_HEALTH.md](./PRV_001_DPIA_HEALTH.md) |
| Evaluation | [PRV_001_DPIA_EVALUATION.md](./PRV_001_DPIA_EVALUATION.md) |
| Public recruitment, candidate OTP/portal, documents, notifications | [PRV_001_DPIA_RECRUITMENT.md](./PRV_001_DPIA_RECRUITMENT.md) |

## Runtime alignment

- PII encryption / redaction: `PiiFieldEncryptionService`, `PiiRedactionMiddleware`
- Tenant purpose boundary: TEN-002
- DSAR intake: Compliance `DsarRequest` (GOV-001 / PRV-002 path)
- Validation script: `scripts/prv001-ropa-validate.ps1`
