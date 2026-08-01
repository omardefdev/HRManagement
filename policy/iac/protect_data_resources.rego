# Flag data-plane PVCs missing destroy-protection annotation (OPS-005).
# Apply via: conftest test -p policy/iac/protect_data_resources.rego deploy/k8s/data/overlays/production-data-ha
package main

deny[msg] {
  input.kind == "PersistentVolumeClaim"
  not input.metadata.annotations["hrmanagement.io/protect-destroy"]
  msg := sprintf(
    "PVC %v should set annotation hrmanagement.io/protect-destroy=true (OPS-005)",
    [input.metadata.name],
  )
}
