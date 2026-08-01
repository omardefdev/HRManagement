# OPS-005 — طوبولوجيا Infrastructure as Code

القرار: [ADR-OPS-005-iac.md](./ADR-OPS-005-iac.md). التحقق: [OPS_IAC_VERIFICATION.md](./OPS_IAC_VERIFICATION.md).

## طبقات الملكية

| طبقة | أداة | محتويات |
|---|---|---|
| Foundation | Terraform `terraform/modules/*` | VPC، EKS، S3، DNS، KMS، IRSA، ESO |
| Workloads | Kustomize `deploy/k8s/**` | Apps HA، data HA، backup CronJobs، DR stubs |
| Secrets | ExternalSecret → AWS SM | أدوار IAM-004 / backup — لا قيم في git للإنتاج |
| Policy | `policy/iac/` + CI | منع plaintext prod، protect destroy، tags |

## البيئات

| Env | مسار TF | Overlay Kustomize |
|---|---|---|
| staging | `terraform/envs/staging` | `staging-ha` + `staging-data-ha` |
| production | `terraform/envs/production` | `production-ha` + `production-data-ha` |
| dr-secondary | `terraform/envs/dr-secondary` | `dr-secondary` + `dr-secondary-data` |

## تدفق clean-room (staging)

```text
iac-bootstrap.sh (state bucket/lock once)
  -> iac-apply-env.sh staging
  -> install ESO / CNPG operator
  -> iac-kustomize-apply.sh staging IMAGE_TAG=...
  -> verify-k8s-ha / verify-k8s-data-ha / verify-backup-catalog
```

## أسرار ومراجع IAM-004

أسماء متوقعة في Secrets Manager (بادئة `/hrmanagement/{env}/`):

| مفتاح بعيد | سر K8s |
|---|---|
| `postgres/app` | `hr-postgres-app` / مقاطع ConnectionStrings |
| `postgres/backup` | `hr-postgres-backup` (`hrm_backup`) |
| `rabbitmq/default` | `rabbitmq-default-user` |
| `rabbitmq/erlang` | `rabbitmq-erlang-cookie` |
| `keycloak/admin` | `keycloak-admin` |
| `keycloak/db` | `keycloak-db` |
| `minio/root` | `minio-root` (تهيئة فقط) |
| `minio/filestorage` | `minio-filestorage` |
| `minio/backup-reader` | `minio-backup-reader` |
| `backup/store` | `cnpg-backup-store` |
| `backup/encryption` | `backup-encryption` |

التطوير المحلي يبقى على `*-placeholder.yaml` مع تعليق **dev-only**.

## Drift وDestroy

- Drift: `scripts/iac-drift-check.sh` (exit 2 عند تغييرات).
- Destroy: `prevent_destroy` على دلاء النسخ/البيانات؛ break-glass موثّق في verification فقط.

## علاقة OPS-004

توفير العنقود الثانوي = `terraform/envs/dr-secondary` ثم apply overlays DR. الإجراءات تبقى في [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md).
