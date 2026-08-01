# OPS-005 — External Secrets Operator bridge
#
# 1. Install ESO (example pin): helm upgrade --install external-secrets external-secrets/external-secrets \
#      -n external-secrets --create-namespace --version 0.9.20
# 2. terraform apply → eso_role_arn
# 3. sed replace ESO_ROLE_ARN in serviceaccount.yaml (or kustomize configMapGenerator)
# 4. kubectl apply -k deploy/k8s/platform/external-secrets
# 5. Apply overlay ExternalSecret CRs (production-ha / production-data-ha)
#
# See docs/OPS_IAC_TOPOLOGY.md for remote key names (IAM-004 roles).
