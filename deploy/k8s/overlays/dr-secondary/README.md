# DR secondary — application plane (OPS-004)
#
# Warm standby for a **separate** Kubernetes cluster in the secondary region.
# Provision that cluster with: terraform/envs/dr-secondary + scripts/iac-apply-env.sh dr-secondary (OPS-005).
# Then: OWNER=… IMAGE_TAG=… bash scripts/iac-kustomize-apply.sh dr-secondary
# Do not apply onto the primary production cluster.
#
# Validate: kubectl kustomize deploy/k8s/overlays/dr-secondary
# Drill DNS host: hr-dr.example.com (see docs/OPS_DR_TOPOLOGY.md)
