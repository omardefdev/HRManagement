# Kubernetes manifests (OPS-001 apps + OPS-004 DR stubs + OPS-005 IaC bridge)

Kustomize layout for **stateless** Gateway + Web apps. Data-plane HA lives in [`data/`](data/) (**OPS-002**). Backup/PITR (**OPS-003**). DR warm-standby stubs (**OPS-004**). Cluster provision + External Secrets (**OPS-005**).

## Layout

```text
deploy/k8s/
  base/
  overlays/
    staging-ha/          # + external-secrets/ (OPS-005)
    production-ha/       # + external-secrets/ (OPS-005)
    dr-secondary/        # OPS-004 warm standby apps (secondary cluster only)
  data/
    overlays/
      staging-data-ha/
      production-data-ha/
      dr-secondary-data/  # OPS-004 warm standby data
  platform/
    external-secrets/    # ClusterSecretStore + IRSA SA
```

## Apply (preferred: IaC scripts)

```bash
# After terraform apply for the env:
OWNER=myorg IMAGE_TAG=abc123def456 bash scripts/iac-kustomize-apply.sh staging
# or production / dr-secondary
```

## Apply (manual, after secrets + IMAGE_TAG)

```bash
# 1) Staging/prod: ExternalSecrets sync from AWS SM (see platform/external-secrets)
#    Local only: kubectl create secret from placeholders (dev-only)

# 2) Set registry owner + immutable SHA from DEP-001
export OWNER=myorg IMAGE_TAG=abc123def456
cd deploy/k8s/overlays/production-ha
kubectl kustomize . \
  | sed "s|ghcr.io/OWNER/hrmanagement/|ghcr.io/${OWNER}/hrmanagement/|g; s|:IMAGE_TAG|:${IMAGE_TAG}|g" \
  | kubectl apply -f -

# 3) Migrate Job first, then roll apps
kubectl -n hrmanagement wait --for=condition=complete job/hr-migrate --timeout=600s
```

## Validate without a cluster

```bash
kubectl kustomize deploy/k8s/overlays/production-ha >/dev/null
kubectl kustomize deploy/k8s/overlays/staging-ha >/dev/null
kubectl kustomize deploy/k8s/overlays/dr-secondary >/dev/null
kubectl kustomize deploy/k8s/data/overlays/dr-secondary-data >/dev/null
OWNER=x IMAGE_TAG=y bash scripts/iac-kustomize-apply.sh staging --dry-run
```

## Notes

- No `container_name` — pods get unique names.
- Probes: liveness `{pathBase}/health/live`, readiness+startup `{pathBase}/health/ready` (UI apps set ASPNETCORE_PATHBASE).
- Graceful shutdown: `ASPNETCORE_SHUTDOWNTIMEOUTSECONDS=30` + `terminationGracePeriodSeconds: 45`.
- YARP destinations: ConfigMap `hr-gateway-yarp` → ClusterIP DNS; `PowerOfTwoChoices` ready for multi-destination.
- HPA is **not** applied to the migrate Job.
- DR secondary: see `overlays/dr-secondary/README.md` and `docs/OPS_DR_TOPOLOGY.md` — provision via `terraform/envs/dr-secondary` (**OPS-005**).
- Verification: `docs/OPS_HA_VERIFICATION.md` / `docs/OPS_IAC_VERIFICATION.md`.
