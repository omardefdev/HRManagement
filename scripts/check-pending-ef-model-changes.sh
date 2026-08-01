#!/usr/bin/env bash
# DB-001-B: Fail if any of the 15 DbContexts has pending model changes vs migrations.
# Requires: dotnet SDK + local tool dotnet-ef (dotnet tool restore).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "ERROR: dotnet not found on PATH."
  exit 1
fi

dotnet tool restore >/dev/null

declare -a MIGRATIONS=(
  "src/Gateway/HRManagement.Gateway:src/Gateway/HRManagement.Gateway"
  "src/Services/HRCore/HR.Core.Infrastructure:src/Services/HRCore/HR.Core.Web"
  "src/Services/HRContracts/HR.Contracts.Infrastructure:src/Services/HRContracts/HR.Contracts.Web"
  "src/Services/Payroll/Payroll.Infrastructure:src/Services/Payroll/Payroll.Web"
  "src/Services/HRHealth/HR.Health.Infrastructure:src/Services/HRHealth/HR.Health.Web"
  "src/Services/HRMovements/HR.Movements.Infrastructure:src/Services/HRMovements/HR.Movements.Web"
  "src/Services/HRTraining/HR.Training.Infrastructure:src/Services/HRTraining/HR.Training.Web"
  "src/Services/IDCard/IDCard.Infrastructure:src/Services/IDCard/IDCard.Web"
  "src/Services/RecordsArchive/Records.Archive.Infrastructure:src/Services/RecordsArchive/Records.Archive.Web"
  "src/Services/AdminLogistics/Admin.Logistics.Infrastructure:src/Services/AdminLogistics/Admin.Logistics.Web"
  "src/Services/ITInfrastructure/IT.Infra.Infrastructure:src/Services/ITInfrastructure/IT.Infra.Web"
  "src/Services/DevAnalytics/Dev.Analytics.Infrastructure:src/Services/DevAnalytics/Dev.Analytics.Web"
  "src/Services/Compliance/Compliance.Infrastructure:src/Services/Compliance/Compliance.Web"
  "src/Services/AuthDirectory/Auth.Directory.Infrastructure:src/Services/AuthDirectory/Auth.Directory.Web"
  "src/Services/FileStorage/File.Storage.Infrastructure:src/Services/FileStorage/File.Storage.Web"
)

failed=0
for entry in "${MIGRATIONS[@]}"; do
  IFS=':' read -r project startup <<< "${entry}"
  echo "Checking pending model changes: ${project} ..."
  # has-pending-model-changes exits 1 when the model differs from the last migration.
  if ! dotnet ef migrations has-pending-model-changes \
      --project "${project}" \
      --startup-project "${startup}" \
      --configuration Release; then
    echo "ERROR: Pending EF model changes for ${project}. Add a migration before merging."
    failed=1
  fi
done

if [ "${failed}" -ne 0 ]; then
  echo "Pending model change gate FAILED."
  exit 1
fi

echo "Pending model change gate passed (${#MIGRATIONS[@]} contexts)."
