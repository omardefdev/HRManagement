# Deny Opaque Secrets with stringData unless marked dev-only (OPS-005).
package main

deny[msg] {
  input.kind == "Secret"
  input.apiVersion == "v1"
  input.stringData
  not input.metadata.labels["hrmanagement.io/secret-source"] == "dev-only-placeholder"
  msg := sprintf(
    "plaintext Secret stringData forbidden for %v/%v — use ExternalSecret (OPS-005)",
    [object.get(input.metadata, "namespace", ""), input.metadata.name],
  )
}
