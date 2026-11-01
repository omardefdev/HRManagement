# Service wiring matrix (generated)

> Generated from `deploy/wiring/service-wiring.json` by `scripts/int037-render-wiring-doc.ps1`. Do not edit by hand:
> change the JSON and re-run the script. `ProductionWiringMatrixTests` fails when this file is stale.

## Contract for later plans (PLAN-37, P-8)

Any plan that adds a service-to-service client, call or scope, changes a server policy for an endpoint that a
service calls, or removes a registration updates `deploy/wiring/service-wiring.json` **in the same commit**.
`ProductionWiringMatrixTests` enforces it: code -> matrix (every registration and URL key), matrix -> server
policies (every call passes with its scope and fails without it; allow-lists admit only matrix callers), matrix ->
deployment (both production compose chains and k8s), prod-like parity, and the Keycloak scripts.
`scripts/int037-wiring-check.ps1` repeats the deployment check on the rendered `docker compose config` and
`kubectl kustomize` output. Known follow-ups: PLAN-42 (`dsar.subject.erase`, new `api/internal/*` policies, a
secret per service, an authenticated legal-hold gate), PLAN-46 (approval start from IDCard and Compliance),
PLAN-49 (removing unused policy-enforce registrations), PLAN-50 (the internal delegation endpoint), PLAN-39
(the production realm generated from this matrix).

## Services

| Service | Keycloak client | Secret variable | URLs | Scopes | Legal packs |
|---|---|---|---|---|---|
| `hr-gateway` | (no service identity; OIDC `hr-gateway-client`) | - | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `Integration__AuthDirectoryBaseUrl=http://auth-directory-web:8080` | - | - |
| `hr-core-web` | `svc-hr-core` | `SVC_HR_CORE_CLIENT_SECRET` | `HrGateway__InternalBaseUrl=http://hr-gateway:8080`, `Integration__PayrollBaseUrl=http://payroll-web:8080`, `Integration__FileStorageBaseUrl=http://file-storage-web:8080` | `file-storage.references`, `payroll.grades.read`, `file-storage.service-upload` | `syr003-service-regimes-v1` |
| `hr-contracts-web` | `svc-hr-contracts` | `SVC_HR_CONTRACTS_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `HrGateway__InternalBaseUrl=http://hr-gateway:8080` | `hr-core.workflow`, `hr-core.lookup`, `file-storage.references` | `syr003-service-regimes-v1` |
| `hr-movements-web` | `svc-hr-movements` | `SVC_HR_MOVEMENTS_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080` | `hr-core.lookup` | `syr003-service-regimes-v1` |
| `payroll-web` | `svc-payroll` | `SVC_PAYROLL_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080` | `hr-core.lookup` | - |
| `idcard-web` | `svc-idcard` | `SVC_IDCARD_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `HrGateway__InternalBaseUrl=http://hr-gateway:8080` | `hr-core.workflow`, `file-storage.references` | - |
| `records-archive-web` | `svc-records-archive` | `SVC_RECORDS_ARCHIVE_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `HrGateway__InternalBaseUrl=http://hr-gateway:8080` | `hr-core.workflow`, `file-storage.references` | - |
| `admin-logistics-web` | `svc-admin-logistics` | `SVC_ADMIN_LOGISTICS_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `HrGateway__InternalBaseUrl=http://hr-gateway:8080` | `hr-core.lookup`, `hr-core.workflow`, `file-storage.references` | - |
| `it-infra-web` | `svc-it-infra` | `SVC_IT_INFRA_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `Services__AuthDirectory__BaseUrl=http://auth-directory-web:8080` | `hr-core.workflow`, `directory.sync` | - |
| `compliance-web` | `svc-compliance` | `SVC_COMPLIANCE_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `Integration__PayrollBaseUrl=http://payroll-web:8080`, `Integration__FileStorageBaseUrl=http://file-storage-web:8080`, `HrGateway__InternalBaseUrl=http://hr-gateway:8080` | `hr-core.lookup`, `hr-core.workflow`, `dsar.subject.read`, `file-storage.references` | - |
| `auth-directory-web` | `svc-auth-directory` | `SVC_AUTH_DIRECTORY_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080` | `hr-core.lookup` | - |
| `hr-health-web` | `svc-hr-health` | `SVC_HR_HEALTH_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080` | - | - |
| `hr-training-web` | `svc-hr-training` | `SVC_HR_TRAINING_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080`, `HrGateway__InternalBaseUrl=http://hr-gateway:8080` | `file-storage.references` | - |
| `dev-analytics-web` | `svc-dev-analytics` | `SVC_DEV_ANALYTICS_CLIENT_SECRET` | `Integration__HrCoreBaseUrl=http://hr-core-web:8080` | - | - |
| `file-storage-web` | (none) | - | - | - | - |

## Calls

| Caller | Registration | Endpoint | Target | Server policies | Scope | Note |
|---|---|---|---|---|---|---|
| `hr-core-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |
| `hr-core-web` | `AddPayrollGradeLookup` | `GET /api/internal/pay-grades/{gradeCode}` | payroll-web | `ServicePolicies.PayrollGradeLookup` | `payroll.grades.read` |  |
| `hr-core-web` | `AddCandidateDocumentUploadClient` | `POST /api/files/service-upload` | file-storage-web | `ServicePolicies.FileStorageServiceUpload` | `file-storage.service-upload` |  |
| `hr-contracts-web` | `AddHrCodeGeneration` | `POST /api/internal/codes/generate` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `hr-contracts-web` | `AddHrPolicyEnforceClient` | `POST /api/internal/policies/enforce` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreBranchOperation` | `hr-core.workflow` |  |
| `hr-contracts-web` | `AddHrCoreLookupClient` | `POST /api/lookup/employees/by-ids` | hr-core-web | `HrManagementPolicies.HrCoreLookup` | `hr-core.lookup` |  |
| `hr-contracts-web` | `AddHrCoreActiveRegimeClient` | `POST /api/internal/service-regime/active-regimes` | hr-core-web | `ServicePolicies.HrCoreLookupS2S` | `hr-core.lookup` |  |
| `hr-contracts-web` | `AddEmployeeLifecycleGate` | `GET /api/internal/employees/{employeeId}/lifecycle-flags` | hr-core-web | `ServicePolicies.HrCoreLookupS2S` | `hr-core.lookup` | no service token yet (PLAN-42) |
| `hr-contracts-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |
| `hr-movements-web` | `AddHrCoreActiveRegimeClient` | `POST /api/internal/service-regime/active-regimes` | hr-core-web | `ServicePolicies.HrCoreLookupS2S` | `hr-core.lookup` |  |
| `payroll-web` | `AddHrCoreLookupClient` | `POST /api/lookup/employees/by-ids` | hr-core-web | `HrManagementPolicies.HrCoreLookup` | `hr-core.lookup` |  |
| `payroll-web` | `AddHrCoreServiceRegimeClient` | `POST /api/internal/service-regime/payroll-blockers` | hr-core-web | `ServicePolicies.HrCoreLookupS2S` | `hr-core.lookup` |  |
| `idcard-web` | `AddHrCodeGeneration` | `POST /api/internal/codes/generate` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `idcard-web` | `AddHrPolicyEnforceClient` | `POST /api/internal/policies/enforce` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreBranchOperation` | `hr-core.workflow` |  |
| `idcard-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |
| `records-archive-web` | `AddHrCodeGeneration` | `POST /api/internal/codes/generate` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `records-archive-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |
| `admin-logistics-web` | `AddHrCodeGeneration` | `POST /api/internal/codes/generate` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `admin-logistics-web` | `AddHrPolicyEnforceClient` | `POST /api/internal/policies/enforce` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreBranchOperation` | `hr-core.workflow` |  |
| `admin-logistics-web` | `AddHrCoreLookupClient` | `POST /api/lookup/employees/by-ids` | hr-core-web | `HrManagementPolicies.HrCoreLookup` | `hr-core.lookup` |  |
| `admin-logistics-web` | `AddHrCoreWorkflowClient` | `POST /api/workflow-instances` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `admin-logistics-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |
| `it-infra-web` | `AddDirectorySyncClient` | `POST /api/internal/directory/sync-employee` | auth-directory-web | `ServicePolicies.DirectorySync` | `directory.sync` |  |
| `it-infra-web` | `AddHrCodeGeneration` | `POST /api/internal/codes/generate` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `compliance-web` | `AddComplianceDsarCollector` | `GET /api/internal/dsar-subjects/{employeeId}` | hr-core-web | `ServicePolicies.DsarSubjectRead` | `dsar.subject.read` |  |
| `compliance-web` | `AddComplianceDsarCollector` | `GET /api/internal/dsar-subjects/{employeeId}` | payroll-web | `ServicePolicies.DsarSubjectRead` | `dsar.subject.read` |  |
| `compliance-web` | `AddComplianceDsarCollector` | `GET /api/internal/dsar-subjects/{employeeId}/files` | file-storage-web | `ServicePolicies.DsarSubjectRead` | `dsar.subject.read` |  |
| `compliance-web` | `AddHrDsarDeletion` | `POST /api/internal/dsar-subjects/candidates/{applicationId}/anonymize` | hr-core-web | `ServicePolicies.DsarSubjectRead` | `dsar.subject.read` |  |
| `compliance-web` | `AddHrCodeGeneration` | `POST /api/internal/codes/generate` | hr-core-web | `HrManagementPolicies.HrCoreReadOnly`, `HrManagementPolicies.HrCoreWrite` | `hr-core.workflow` |  |
| `compliance-web` | `AddHrCoreLookupClient` | `POST /api/lookup/employees/by-ids` | hr-core-web | `HrManagementPolicies.HrCoreLookup` | `hr-core.lookup` |  |
| `compliance-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |
| `auth-directory-web` | `AddHrCoreLookupClient` | `POST /api/lookup/employees/by-ids` | hr-core-web | `HrManagementPolicies.HrCoreLookup` | `hr-core.lookup` |  |
| `hr-training-web` | `AddHrFileReferenceClient` | `POST /api/files/{fileId}/references` | file-storage-web (via hr-gateway) | `HrManagementPolicies.ModuleFileStorage`, `ServicePolicies.FileStorageReferences` | `file-storage.references` |  |

## Registrations

| Registration | Defined in | URL keys | Registered by |
|---|---|---|---|
| `AddHrCodeGeneration` | `src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs` | `Integration__HrCoreBaseUrl` | `hr-contracts-web`, `idcard-web`, `records-archive-web`, `admin-logistics-web`, `it-infra-web`, `compliance-web` |
| `AddHrPolicyEnforceClient` | `src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs` | `Integration__HrCoreBaseUrl` | `hr-contracts-web`, `hr-movements-web`, `payroll-web`, `idcard-web`, `records-archive-web`, `admin-logistics-web`, `it-infra-web`, `compliance-web`, `hr-health-web`, `hr-training-web`, `dev-analytics-web` |
| `AddHrCoreLookupClient` | `src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreLookupClient.cs` | `Integration__HrCoreBaseUrl` | `hr-contracts-web`, `payroll-web`, `admin-logistics-web`, `compliance-web`, `auth-directory-web` |
| `AddHrCoreActiveRegimeClient` | `src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreActiveRegimeClient.cs` | `Integration__HrCoreBaseUrl` | `hr-contracts-web`, `hr-movements-web` |
| `AddHrCoreServiceRegimeClient` | `src/Shared/HRManagement.ServiceDefaults/Integration/HrCoreServiceRegimeClient.cs` | `Integration__HrCoreBaseUrl` | `payroll-web` |
| `AddHrDsarDeletion` | `src/Shared/HRManagement.ServiceDefaults/Privacy/DsarDeletionOrchestrator.cs` | `Integration__HrCoreBaseUrl` | `compliance-web` |
| `AddHrFileReferenceClient` | `src/Shared/HRManagement.ServiceDefaults/FileReferenceClient.cs` | `HrGateway__InternalBaseUrl` | `hr-core-web`, `hr-contracts-web`, `idcard-web`, `records-archive-web`, `admin-logistics-web`, `compliance-web`, `hr-training-web` |
| `AddPayrollGradeLookup` | `src/Services/HRCore/HR.Core.Infrastructure/Integration/PayrollGradeLookup.cs` | `Integration__PayrollBaseUrl` | `hr-core-web` |
| `AddCandidateDocumentUploadClient` | `src/Services/HRCore/HR.Core.Web/Services/CandidateDocumentUploadClient.cs` | `Integration__FileStorageBaseUrl` | `hr-core-web` |
| `AddComplianceDsarCollector` | `src/Services/Compliance/Compliance.Infrastructure/Privacy/HttpDsarSubjectSources.cs` | `Integration__HrCoreBaseUrl`, `Integration__PayrollBaseUrl`, `Integration__FileStorageBaseUrl` | `compliance-web` |
| `AddHrCoreWorkflowClient` | `src/Services/AdminLogistics/Admin.Logistics.Infrastructure/DependencyInjection.cs` | `Integration__HrCoreBaseUrl` | `admin-logistics-web` |
| `AddDirectorySyncClient` | `src/Services/ITInfrastructure/IT.Infra.Infrastructure/Integration/HttpDirectoryEntrySyncClient.cs` | `Services__AuthDirectory__BaseUrl` | `it-infra-web` |
| `AddEmployeeLifecycleGate` | `src/Services/HRContracts/HR.Contracts.Infrastructure/DependencyInjection.cs` | `Integration__HrCoreBaseUrl` | `hr-contracts-web` |

Registered but never injected (they keep the URL, get no scope for it; removal is PLAN-49):

- `hr-movements-web`: `AddHrPolicyEnforceClient` - registered, never injected: URL only, no hr-core.workflow (D-INT-4); removal is PLAN-49
- `payroll-web`: `AddHrPolicyEnforceClient` - registered, never injected: URL only, no hr-core.workflow (D-INT-4); removal is PLAN-49
- `records-archive-web`: `AddHrPolicyEnforceClient` - registered, never injected: no scope is granted for it (D-INT-4); removal is PLAN-49
- `it-infra-web`: `AddHrPolicyEnforceClient` - registered, never injected (D-INT-4); hr-core.workflow is granted for code generation, not for it; removal is PLAN-49
- `compliance-web`: `AddHrPolicyEnforceClient` - registered, never injected (D-INT-4); hr-core.workflow is granted for code generation, not for it; removal is PLAN-49
- `hr-health-web`: `AddHrPolicyEnforceClient` - registered, never injected: URL only, no scope (D-INT-4); removal is PLAN-49
- `hr-training-web`: `AddHrPolicyEnforceClient` - registered, never injected: URL only, no hr-core.workflow (D-INT-4); removal is PLAN-49
- `dev-analytics-web`: `AddHrPolicyEnforceClient` - registered, never injected: URL only, no scope (D-INT-4); removal is PLAN-49

## Scopes

| Scope | Audiences | Granted to | Description |
|---|---|---|---|
| `hr-core.lookup` | `api-hr-core` | `svc-hr-contracts`, `svc-hr-movements`, `svc-payroll`, `svc-admin-logistics`, `svc-compliance`, `svc-auth-directory` | HR.Core employee lookup (S2S) |
| `hr-core.workflow` | `api-hr-core` | `svc-hr-contracts`, `svc-idcard`, `svc-records-archive`, `svc-admin-logistics`, `svc-it-infra`, `svc-compliance` | HR.Core workflow + code generation (S2S) |
| `file-storage.references` | `api-file-storage` | `svc-hr-core`, `svc-hr-contracts`, `svc-idcard`, `svc-records-archive`, `svc-admin-logistics`, `svc-compliance`, `svc-hr-training` | File.Storage reference registration (S2S) |
| `dsar.subject.read` | `api-hr-core`, `api-payroll`, `api-file-storage` | `svc-compliance` | Compliance DSAR subject collection (S2S) |
| `payroll.grades.read` | `api-payroll` | `svc-hr-core` | HR.Core offer salary bounds from Payroll pay grades (S2S, HCM-027) |
| `file-storage.service-upload` | `api-file-storage` | `svc-hr-core` | HR.Core relays candidate document uploads to File.Storage (S2S, PLAN-30) |
| `directory.sync` | `api-auth-directory` | `svc-it-infra` | Auth.Directory synchronization (S2S) |
| `evidence.ingest` | `hr-gateway-client` | - | Gateway evidence ingestion (S2S) |

## Allow-list entries without a call (not widened; removal is PLAN-42)

- `ServicePolicies.HrCoreLookupS2S`: `hr-gateway-client`, `svc-admin-logistics`, `svc-compliance` - Allowed before PLAN-37 without a call to the lifecycle or service-regime endpoints (their lookups use /api/lookup under HrManagementPolicies.HrCoreLookup). Not widened here; removing them is PLAN-42 (least privilege).
- `ServicePolicies.DirectorySync`: `svc-hr-core`, `svc-auth-directory` - Allowed before PLAN-37 without a directory-sync call. Not widened here; removing them is PLAN-42.

## Legal packs

- `syr003-service-regimes-v1`: source `docs/syr/service-regimes.json`, shipped as `seed-packs/syr003-service-regimes-v1/regimes.json` in `hr-core-web`, `hr-contracts-web`, `hr-movements-web`
