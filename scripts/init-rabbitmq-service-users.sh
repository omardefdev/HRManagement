#!/usr/bin/env bash
# IAM-004: create per-service RabbitMQ users with least-privilege configure patterns.
# Run after RabbitMQ is healthy. Requires rabbitmqctl inside the container.
#
#   ./scripts/init-rabbitmq-service-users.sh
#   RABBITMQ_CONTAINER=hrm-rabbitmq ./scripts/init-rabbitmq-service-users.sh
set -euo pipefail

CONTAINER="${RABBITMQ_CONTAINER:-hrm-rabbitmq}"
DEFAULT_PASS="${RABBITMQ_SERVICE_PASSWORD:-${RABBITMQ_DEFAULT_PASS:?RABBITMQ_DEFAULT_PASS or RABBITMQ_SERVICE_PASSWORD required}}"

pass_for() {
  local specific="${1:-}"
  if [ -n "$specific" ]; then echo "$specific"; else echo "$DEFAULT_PASS"; fi
}

create_user() {
  local user="$1"
  local pass="$2"
  local configure="$3"

  docker exec "$CONTAINER" rabbitmqctl list_users | grep -q "^${user}\\b" \
    && docker exec "$CONTAINER" rabbitmqctl change_password "$user" "$pass" \
    || docker exec "$CONTAINER" rabbitmqctl add_user "$user" "$pass"

  docker exec "$CONTAINER" rabbitmqctl set_permissions -p / "$user" "$configure" ".*" ".*"
  echo "OK: $user (configure=$configure)"
}

create_user mq_hr_core "$(pass_for "${RABBITMQ_HR_CORE_PASS:-}")" '^(HR\.Core|hr-core|MassTransit).*'
create_user mq_hr_contracts "$(pass_for "${RABBITMQ_HR_CONTRACTS_PASS:-}")" '^(HR\.Contracts|hr-contracts|MassTransit).*'
create_user mq_hr_health "$(pass_for "${RABBITMQ_HR_HEALTH_PASS:-}")" '^(HR\.Health|hr-health|MassTransit).*'
create_user mq_hr_movements "$(pass_for "${RABBITMQ_HR_MOVEMENTS_PASS:-}")" '^(HR\.Movements|hr-movements|MassTransit).*'
create_user mq_hr_training "$(pass_for "${RABBITMQ_HR_TRAINING_PASS:-}")" '^(HR\.Training|hr-training|MassTransit).*'
create_user mq_payroll "$(pass_for "${RABBITMQ_PAYROLL_PASS:-}")" '^(Payroll|payroll|MassTransit).*'
create_user mq_idcard "$(pass_for "${RABBITMQ_IDCARD_PASS:-}")" '^(IDCard|idcard|MassTransit).*'
create_user mq_records_archive "$(pass_for "${RABBITMQ_RECORDS_ARCHIVE_PASS:-}")" '^(Records\.Archive|records-archive|MassTransit).*'
create_user mq_admin_logistics "$(pass_for "${RABBITMQ_ADMIN_LOGISTICS_PASS:-}")" '^(Admin\.Logistics|admin-logistics|MassTransit).*'
create_user mq_it_infra "$(pass_for "${RABBITMQ_IT_INFRA_PASS:-}")" '^(IT\.Infra|it-infra|MassTransit).*'
create_user mq_dev_analytics "$(pass_for "${RABBITMQ_DEV_ANALYTICS_PASS:-}")" '^(Dev\.Analytics|dev-analytics|MassTransit).*'
create_user mq_compliance "$(pass_for "${RABBITMQ_COMPLIANCE_PASS:-}")" '^(Compliance|compliance|MassTransit).*'
create_user mq_auth_directory "$(pass_for "${RABBITMQ_AUTH_DIRECTORY_PASS:-}")" '^(Auth\.Directory|auth-directory|MassTransit).*'
create_user mq_file_storage "$(pass_for "${RABBITMQ_FILE_STORAGE_PASS:-}")" '^(File\.Storage|file-storage|MassTransit).*'
create_user mq_gateway "$(pass_for "${RABBITMQ_GATEWAY_PASS:-}")" '^(HRManagement\.Gateway|gateway|MassTransit).*'

echo "IAM-004: RabbitMQ service users ready."
echo "Note: RABBITMQ_DEFAULT_USER remains the bootstrap/admin account — do not use it in app env."
