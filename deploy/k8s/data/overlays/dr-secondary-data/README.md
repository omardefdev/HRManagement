# DR secondary — data plane (OPS-004)
#
# Warm standby. Bootstrap from OPS-003 object store; no shared disks with primary.
# Validate: kubectl kustomize deploy/k8s/data/overlays/dr-secondary-data
# Secondary cluster provision = OPS-005.
