# PLAN-37 baseline (P0, 2026-10-31/11-01 UTC)

Raw dumps (git-ignored): `artifacts/plan37/baseline/` (`production.json`, `single-host.json`, `acceptance.json`,
`prodlike.json`, `k8s-*.yaml`, `wiring-env-summary.md`, `build.txt`, `unit.txt`), server: `artifacts/plan37/server/`.

## 0.2 Build and unit filters

- `dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors.
- `dotnet test src/Tests/HRManagement.UnitTests --filter Architecture|CodeGeneration|Syr00|Sec001|Hard001`
  (FullyQualifiedName~ form): **204 passed, 0 failed**.

## 0.3 What the code reads (service x key x registration x use)

One token per process: `ClientCredentialsTokenProvider` reads one `ServiceIdentity:ClientId` / `Scope`; every
authenticated named client of a service sends the same scope string.

| Service | Registrations used (injected) | Registered, never injected | URL keys read | Calls -> server policy -> scope |
|---|---|---|---|---|
| hr-core-web | FileReference (via gateway), PayrollGradeLookup, CandidateDocumentUpload | **PolicyEnforce, CodeGeneration (Program.cs:277-278)** | `HrGateway:InternalBaseUrl`, `Integration:PayrollBaseUrl`, `Integration:FileStorageBaseUrl` | file refs -> Service.FileStorageReferences -> file-storage.references; pay grades -> Service.PayrollGradeLookup -> payroll.grades.read; service upload -> Service.FileStorageServiceUpload -> file-storage.service-upload |
| hr-contracts-web | CodeGeneration, FileReference, HrCoreLookup, ActiveRegime, PolicyEnforce, lifecycle gate | - | `Integration:HrCoreBaseUrl`, `HrGateway:InternalBaseUrl`, lifecycle gate: `Services:HRCore:BaseUrl` / `HrCore:BaseUrl` | codes -> HrCoreWrite -> hr-core.workflow; enforce -> HrCoreBranchOperation -> hr-core.workflow; lookup -> HrCoreLookup -> hr-core.lookup; active regimes + lifecycle flags -> Service.HrCoreLookup -> hr-core.lookup; file refs -> file-storage.references |
| hr-movements-web | ActiveRegime | PolicyEnforce | `Integration:HrCoreBaseUrl` | active regimes -> Service.HrCoreLookup -> hr-core.lookup |
| payroll-web | HrCoreLookup, ServiceRegime (payroll blockers) | PolicyEnforce | `Integration:HrCoreBaseUrl` | lookup -> hr-core.lookup; blockers -> Service.HrCoreLookup -> hr-core.lookup |
| idcard-web | CodeGeneration, FileReference, PolicyEnforce | - | `Integration:HrCoreBaseUrl`, `HrGateway:InternalBaseUrl` | codes, enforce -> hr-core.workflow; file refs |
| records-archive-web | CodeGeneration (6 consumers), FileReference | PolicyEnforce | `Integration:HrCoreBaseUrl`, `HrGateway:InternalBaseUrl` | codes -> hr-core.workflow; file refs |
| admin-logistics-web | CodeGeneration (registered twice: Program.cs:29, Infra DI:22), PolicyEnforce, HrCoreLookup, HrCoreWorkflow (inline), FileReference | - | `Integration:HrCoreBaseUrl`, `HrGateway:InternalBaseUrl` | codes, enforce, workflow-instances -> hr-core.workflow; lookup; file refs |
| it-infra-web | DirectorySync (inline), CodeGeneration | PolicyEnforce | `Integration:HrCoreBaseUrl`, `Services:AuthDirectory:BaseUrl` | sync -> Service.DirectorySync -> directory.sync; codes -> hr-core.workflow |
| compliance-web | DsarDeletion, DsarCollector (HR.Core, Payroll, FileStorage), FileReference, CodeGeneration, HrCoreLookup | PolicyEnforce | `Integration:HrCoreBaseUrl`, `Integration:PayrollBaseUrl`, `Integration:FileStorageBaseUrl`, `HrGateway:InternalBaseUrl` | DSAR read/anonymize -> Service.DsarSubjectRead -> dsar.subject.read; codes -> hr-core.workflow; lookup -> hr-core.lookup; file refs -> file-storage.references (**svc-compliance not in the allow-list**) |
| auth-directory-web | HrCoreLookup | - | `Integration:HrCoreBaseUrl` | lookup by-ids -> **HrCoreLookup** (scope only, no allow-list) -> hr-core.lookup |
| hr-training-web | FileReference | PolicyEnforce | `Integration:HrCoreBaseUrl`, `HrGateway:InternalBaseUrl` | file refs |
| hr-health-web, dev-analytics-web | - | PolicyEnforce | `Integration:HrCoreBaseUrl` | none |
| hr-gateway | `hr-core-journey` + stats calls with the **user's** token, no ServiceIdentity | - | `Integration:HrCoreBaseUrl` (Program.cs:126, fallback localhost:5010), `Integration:AuthDirectoryBaseUrl` (HomeController.cs:35, fallback localhost:5130) | none S2S |
| file-storage-web | none | - | - | - |

Missing-URL behaviour today: CodeGeneration / PolicyEnforce / HrCoreLookup / DsarDeletion / DsarCollector /
Admin.Logistics workflow / IT.Infra directory sync register a client without a BaseAddress (or skip the call);
HrCoreLookup, ActiveRegime, ServiceRegime and the gateway fall back to `http://localhost:5010`; FileReference throws at
start-up (the pattern this plan generalizes); PayrollGradeLookup / CandidateDocumentUpload register an "unavailable"
implementation.

### Differences from the plan's §1 table, explained

1. **Auth.Directory and the S2S allow-list.** Its only call (`/api/lookup/employees/by-ids`) is under the
   `HrCoreLookup` policy (scope only), not `Service.HrCoreLookup` (the allow-listed policy on the lifecycle and
   service-regime endpoints). Adding `svc-auth-directory` to that allow-list is not justified by any call, so it is
   **not added** (deviation; rule 3 of §1 "no client the matrix does not justify" wins). Auth.Directory gets
   `ServiceIdentity__ClientId=svc-auth-directory` and `hr-core.lookup`, which is all its call needs.
2. **Allow-list entries without a call** (left as they are; removing privileges is PLAN-42/49 work): `hr-gateway-client`,
   `svc-admin-logistics`, `svc-compliance` in `Service.HrCoreLookup` (they call `/api/lookup/*`, not the S2S
   endpoints); `svc-hr-core`, `svc-auth-directory` in `Service.DirectorySync`. The matrix lists them under
   `allowListExceptions` with the reason; any other unjustified entry fails the check.
3. **HR.Contracts lifecycle gate** is a plain `AddHttpClient` without a service token (Infra DI:39). This plan changes
   only its key and wiring (D-INT-3/§4); the call is in the matrix with `authenticated: false` and owner PLAN-42.
4. **Gateway:** no service identity (user tokens only); the matrix entry has URLs and `serviceIdentity: false`. Its two
   localhost fallbacks are removed with the same endpoint helper (DoD: no localhost fallback in production code).
5. **IT.Infra directory sync** silently reports success when `Services:AuthDirectory:BaseUrl` is unset; it goes through
   the same required-URL helper (rule 1 of §4: no S2S client registered without a URL outside Development).
6. **HR.Contracts' appsettings scope** is only `file-storage.references`; the code needs `hr-core.workflow` and
   `hr-core.lookup` too (matches the plan's table). HR.Core's appsettings lacks `payroll.grades.read` (plan table has it).
7. Unused PolicyEnforce registrations in Payroll, Movements, Records.Archive, IT.Infra, Compliance, Training, Health,
   Dev.Analytics: they keep the URL and get no `hr-core.workflow` for it (D-INT-4); removal is PLAN-49.

## 0.4 What the deployment sets (effective environment)

Chains from `scripts/vps/lint-compose.ps1` rendered with its lint-only `.env` (both `[PASS]`).

| Service | production / single-host today | acceptance (lab) adds |
|---|---|---|
| hr-core-web | HrGateway, ClientId, Scope=`file-storage.references` | Payroll + FileStorage URLs, Scope `+payroll.grades.read +file-storage.service-upload` |
| hr-contracts-web | HrGateway, ClientId, Scope=`file-storage.references`; **no HrCoreBaseUrl** | HrCoreBaseUrl, Scope `+hr-core.lookup +hr-core.workflow` |
| hr-movements-web | ClientId, Scope=`hr-core.lookup`; **no HrCoreBaseUrl** | - |
| payroll-web | HrCoreBaseUrl, ClientId, Scope=`hr-core.lookup` | - |
| idcard-web, records-archive-web | HrGateway, ClientId, Scope=`file-storage.references`; **no HrCoreBaseUrl** | - |
| admin-logistics-web | complete | - |
| it-infra-web | AuthDirectory URL, ClientId, Scope=`directory.sync`; **no HrCoreBaseUrl, no hr-core.workflow** | - |
| compliance-web | 4 URLs, ClientId, Scope=`hr-core.lookup dsar.subject.read` (**no hr-core.workflow, no file-storage.references**) | - |
| auth-directory-web | **no ClientId, no HrCoreBaseUrl** | - |
| hr-health-web, dev-analytics-web | ClientId, no Scope; **no HrCoreBaseUrl** | - |
| hr-training-web | HrGateway, ClientId, Scope=`file-storage.references`; **no HrCoreBaseUrl** | - |
| hr-gateway | both Integration URLs | - |

The prod-like chain (`acceptance.yml + acceptance-prodlike.yml`) has exactly the acceptance values (it inherits them).

k8s (`kubectl kustomize`, all three overlays render): **no** `Integration__*` / `ServiceIdentity__*` /
`HrGateway__InternalBaseUrl` env on any app Deployment; only the gateway's `hr-yarp-config` has the two Integration
URLs. `hr-app-secrets` (production-ha, staging-ha) maps 12 `SVC_*` keys; **no `SVC_AUTH_DIRECTORY_CLIENT_SECRET`**.

VpsDemo realm (template): service clients exist without optional scopes (only svc-admin-logistics has them); missing
clients svc-hr-health, svc-hr-movements, svc-dev-analytics, svc-compliance; missing scopes dsar.subject.read,
payroll.grades.read, file-storage.service-upload, directory.sync, evidence.ingest; svc-auth-directory and
svc-file-storage are disabled.

## 0.5 Legal pack in the images

Images `00926107a37b` (the PLAN-36 release): `/app/seed-packs` **absent** in hr-contracts-web and hr-movements-web;
present in hr-core-web (`syr003-service-regimes-v1/regimes.json`).

## Server before-picture (read-only)

`smoke.sh --require-integration-wiring` on release `00926107a37b`: **pass 16, fail 0, warn 0, deferred 5**:
1. hr-core Integration__PayrollBaseUrl/FileStorageBaseUrl and scopes;
2. Integration__HrCoreBaseUrl for Contracts, IDCard, Archive, IT;
3. S2S token issue per service client;
4. candidate document upload to File.Storage (S2S);
5. per-service SVC_* secrets for every client.
