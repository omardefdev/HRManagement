# HARD-001 — Kubernetes Hardening Baseline

**Control:** HARD-001 | **Asset:** Kubernetes cluster (app + data planes) | **Owner:** Platform Security

## Scope

Manifests under `deploy/k8s/` — base, overlays, data, platform.

## Pod Security Admission (PSA)

| Namespace | enforce | audit | warn |
|---|---|---|---|
| `hrmanagement` | `restricted` | `restricted` | `restricted` |
| `hrmanagement-data` | `restricted` | `restricted` | `restricted` |
| `hrmanagement-observability` | `baseline` | `restricted` | `restricted` |

Enforcement labels: `deploy/k8s/base/pod-security/restricted-psa.yaml`

## RBAC

- ServiceAccounts per workload — no default SA for app pods.
- `Role`/`RoleBinding` scoped to namespace; cluster-admin only for break-glass (documented).
- External Secrets operator SA: read secrets from provider only.
- CNPG / RabbitMQ operator RBAC: `deploy/k8s/data/base/rabbitmq/rbac-pdb.yaml` pattern.

## Network policies

Default deny + explicit allow rules in `deploy/k8s/base/network-policies/`:

- DNS egress
- Gateway → apps
- Apps → data plane
- Observability scrape paths

## Security context defaults

Reference ConfigMap: `deploy/k8s/base/pod-security/security-context-defaults.yaml`

Required pod-level:

```yaml
securityContext:
  runAsNonRoot: true
  seccompProfile:
    type: RuntimeDefault
```

Required container-level:

```yaml
securityContext:
  allowPrivilegeEscalation: false
  readOnlyRootFilesystem: true
  capabilities:
    drop: ["ALL"]
```

Apply `readOnlyRootFilesystem: false` only where emptyDir/tmp volume is mounted (document in exception register).

## Resource governance

- Requests and limits on every container.
- PodDisruptionBudgets in HA overlays (`pdb.yaml`).
- HPA where applicable — no unbounded replica growth without quota.

## Secrets

- No plaintext Secrets in git — placeholders + External Secrets (SUP-001).
- Rotate via ESO refresh interval.

## Drift & compliance

- `scripts/hard001-compliance-scan.ps1` — PSA labels, security context reference, compose hardening.
- `.github/workflows/hardening-check.yml` — CI gate on PR/main.

## Exceptions

See [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md).

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
