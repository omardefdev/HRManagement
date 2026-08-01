# Terraform — HRManagement (OPS-005)

Infrastructure as Code for AWS (first cloud). Workloads remain Kustomize under `deploy/k8s/`.

## Layout

```text
terraform/
  modules/
    network/        # VPC + subnets multi-AZ
    eks/            # EKS cluster + node group
    object_store/   # S3 buckets (backups) with prevent_destroy
    dns/            # Route53 record stubs
    secrets_bridge/ # IRSA role for External Secrets
    irsa/           # generic IRSA helper
  envs/
    staging/
    production/
    dr-secondary/
  backend.hcl.example
```

## Rules

- Never commit `*.tfstate`, `*.tfvars` with secrets, or real kubeconfig.
- Use `*.tfvars.example` only.
- Production apply: GitHub Environment approval (see `.github/workflows/iac-plan.yml` / `iac-apply.yml`).
- After cluster is up: `scripts/iac-kustomize-apply.sh <env>`.

## Quick validate (no cloud credentials)

```bash
cd terraform/envs/staging
terraform init -backend=false
terraform validate
```
