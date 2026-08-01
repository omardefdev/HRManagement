# OPS-005 — تحقق Infrastructure as Code

القرار: [ADR-OPS-005-iac.md](./ADR-OPS-005-iac.md). الطوبولوجيا: [OPS_IAC_TOPOLOGY.md](./OPS_IAC_TOPOLOGY.md).

## بوابة الإغلاق (بدون حساب AWS)

| تحقق | أمر / دليل |
|---|---|
| fmt / validate لكل env | `terraform -chdir=terraform/envs/staging init -backend=false && terraform validate` (كذلك production، dr-secondary) |
| Conftest يرفض plaintext | `conftest test -p policy/iac policy/iac/testdata/bad-prod-secret.yaml` → فشل |
| Conftest يقبل dev-only | `conftest test -p policy/iac policy/iac/testdata/ok-dev-placeholder.yaml` → نجاح |
| prevent_destroy | يظهر على `aws_s3_bucket.backups` و`aws_eks_node_group.default` في الوحدات |
| kustomize dry-run | `OWNER=x IMAGE_TAG=y bash scripts/iac-kustomize-apply.sh staging --dry-run` |
| workflow | `.github/workflows/iac-plan.yml` + `iac-apply.yml` (Environment approval) |

## Clean-room staging (عند توفر حساب)

1. `REGION=… BUCKET=… LOCK_TABLE=… bash scripts/iac-bootstrap.sh`
2. نسخ `terraform/backend.hcl.example` → `backend.hcl` + `envs/staging/terraform.tfvars`
3. `bash scripts/iac-apply-env.sh staging`
4. تثبيت ESO + `kubectl apply -k deploy/k8s/platform/external-secrets` (بعد استبدال `ESO_ROLE_ARN`)
5. ملء Secrets Manager وفق [OPS_IAC_TOPOLOGY.md](./OPS_IAC_TOPOLOGY.md)
6. `OWNER=… IMAGE_TAG=… bash scripts/iac-kustomize-apply.sh staging`
7. `scripts/verify-k8s-ha.sh` / `verify-k8s-data-ha.sh` / `verify-backup-catalog.sh`

## حقن Drift

1. تغيير يدوي في Console (مثلاً tag على VPC) **أو** تعديل tfvars محلياً دون apply.
2. `bash scripts/iac-drift-check.sh staging` → exit **2**.
3. إصلاح عبر PR + plan؛ لا apply يدوي للإنتاج.

## Disaster bootstrap

1. عنقود جديد / حساب فارغ + remote state موجود.
2. `iac-apply-env.sh <env>` يعيد الشبكة/EKS/S3 من state.
3. `iac-kustomize-apply.sh` يعيد overlays OPS-001..004.
4. استعادة البيانات: إجراءات OPS-003/004 (PITR / DR drill) — ليست جزءاً من Terraform.

## Break-glass destroy

إزالة `lifecycle.prevent_destroy` تتطلب PR صريحاً + موافقة production Environment + توثيق في تذكرة. لا `terraform destroy` على دلو `hrm-backups*` من اللابتوب.

## DR secondary

`terraform/envs/dr-secondary` → ثم overlays `dr-secondary` / `dr-secondary-data` ([OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md)).

## REL-001

Staging الناتج عن هذا المسار هو هدف اختبارات production-like (REL-001).
