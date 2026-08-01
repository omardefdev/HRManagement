#!/usr/bin/env bash
# DB-001-C: Build EF Core migration bundles for all 15 DbContexts into artifacts/efbundles/.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT}"

OUT_DIR="${EFBUNDLE_DIR:-${ROOT}/artifacts/efbundles}"
mkdir -p "${OUT_DIR}"

dotnet tool restore >/dev/null

# name|project|startup
declare -a BUNDLES=(
  "gateway|src/Gateway/HRManagement.Gateway|src/Gateway/HRManagement.Gateway"
  "hr-core|src/Services/HRCore/HR.Core.Infrastructure|src/Services/HRCore/HR.Core.Web"
  "hr-contracts|src/Services/HRContracts/HR.Contracts.Infrastructure|src/Services/HRContracts/HR.Contracts.Web"
  "payroll|src/Services/Payroll/Payroll.Infrastructure|src/Services/Payroll/Payroll.Web"
  "hr-health|src/Services/HRHealth/HR.Health.Infrastructure|src/Services/HRHealth/HR.Health.Web"
  "hr-movements|src/Services/HRMovements/HR.Movements.Infrastructure|src/Services/HRMovements/HR.Movements.Web"
  "hr-training|src/Services/HRTraining/HR.Training.Infrastructure|src/Services/HRTraining/HR.Training.Web"
  "idcard|src/Services/IDCard/IDCard.Infrastructure|src/Services/IDCard/IDCard.Web"
  "records-archive|src/Services/RecordsArchive/Records.Archive.Infrastructure|src/Services/RecordsArchive/Records.Archive.Web"
  "admin-logistics|src/Services/AdminLogistics/Admin.Logistics.Infrastructure|src/Services/AdminLogistics/Admin.Logistics.Web"
  "it-infra|src/Services/ITInfrastructure/IT.Infra.Infrastructure|src/Services/ITInfrastructure/IT.Infra.Web"
  "dev-analytics|src/Services/DevAnalytics/Dev.Analytics.Infrastructure|src/Services/DevAnalytics/Dev.Analytics.Web"
  "compliance|src/Services/Compliance/Compliance.Infrastructure|src/Services/Compliance/Compliance.Web"
  "auth-directory|src/Services/AuthDirectory/Auth.Directory.Infrastructure|src/Services/AuthDirectory/Auth.Directory.Web"
  "file-storage|src/Services/FileStorage/File.Storage.Infrastructure|src/Services/FileStorage/File.Storage.Web"
)

for entry in "${BUNDLES[@]}"; do
  IFS='|' read -r name project startup <<< "${entry}"
  out="${OUT_DIR}/${name}"
  echo "Building efbundle ${name} -> ${out} ..."
  rm -f "${out}" "${out}.exe"
  if ! dotnet ef migrations bundle \
      --project "${project}" \
      --startup-project "${startup}" \
      --configuration Release \
      --output "${out}" \
      --force; then
    echo "ERROR: Failed to build efbundle for ${name}"
    exit 1
  fi
done

echo "All EF bundles written to ${OUT_DIR}"
ls -la "${OUT_DIR}"
