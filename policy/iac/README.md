# OPS-005 Conftest / OPA policies for IaC + Kustomize secrets.
#
# Local:
#   conftest test --policy policy/iac policy/iac/testdata/bad-prod-secret.yaml   # expect FAIL
#   conftest test --policy policy/iac policy/iac/testdata/ok-dev-placeholder.yaml # expect PASS
#
# CI also scans deploy/k8s/overlays/production* for Secret stringData without dev-only label.
