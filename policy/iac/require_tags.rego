# Require env + owner tags on Terraform plan resources (when plan JSON is provided).
package terraform.tags

required := {"env", "owner"}

deny[msg] {
  resource := input.resource_changes[_]
  resource.change.actions[_] != "delete"
  tags := object.get(resource.change.after, "tags_all", object.get(resource.change.after, "tags", {}))
  required_tag := required[_]
  not tags[required_tag]
  msg := sprintf("resource %s missing required tag %s", [resource.address, required_tag])
}
