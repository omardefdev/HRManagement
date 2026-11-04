# PLAN-42 baseline (P0)

Measured on the tip of `release/plan-41` (3021ea9a) while PR 36's last check ran: `main` after that merge differs from this tree only by the
evidence commit. Started 2026-11-04.

## 0.2 Build and migrations
- `dotnet build HRManagement.slnx -warnaserror`: 0 errors, 0 warnings, 65 s.
- `dotnet ef migrations has-pending-model-changes` (HR.Core, repository tool manifest): no changes.

## 0.3 Baseline tests
- Unit (`PolicyEngine|PolicyOrchestrator|GatewayPathAuthorization|WebSecurityHelpers|AssetClassifier|Hcm025|Prod001`): 184 passed, 0 failed.
- Integration (`FakePolicyEnforceClient|Fun002SeparationRecord|CodeGenerationConcurrency|CrossTenantRls`), without `RUN_TESTCONTAINERS`: 8 passed, 19 skipped, 0 failed. The 19 are `Skip.IfNot(PostgreSqlFixture.IsEnabled, ...)`.
- The same filter with `RUN_TESTCONTAINERS=true`: 27 passed, 0 skipped, 0 failed (24 s). This is the baseline to compare with.

## 0.4 What PLAN-37 delivered
| Item | hr-contracts | idcard | admin-logistics |
|---|---|---|---|
| `Integration__HrCoreBaseUrl` in k8s `wiring-patch.yaml` | yes | yes | yes |
| `ServiceIdentity__ClientId` in `docker-compose.production.yml` | svc-hr-contracts | svc-idcard | svc-admin-logistics |
| `ServiceIdentity__Scope` in k8s `wiring-patch.yaml` | hr-core.workflow hr-core.lookup file-storage.references | hr-core.workflow file-storage.references | hr-core.lookup hr-core.workflow file-storage.references |
- The compose URLs and scopes live in the shared files, `ProductionWiringMatrixTests` compares them with `deploy/wiring/service-wiring.json`.
- The legal-hold gate registers with `Integration:HrCoreBaseUrl` (`AddEmployeeLifecycleGate`), plain `HttpClient`, no service token: PLAN-42 §7.

## 0.5 Signers of `GatewayCallerProof.Sign`
The plan counted 5; there are 7 call sites.
| Site | Who | PLAN-42 |
|---|---|---|
| `GatewayAccessTokenTransform.cs:109` | gateway, every proxied request | SignV2 |
| `Gateway/Controllers/HomeController.cs:80` | gateway, dashboard stats with the user's token | SignV2 (the plan does not name it) |
| `HrCoreLookupClient.cs:50` | service client | stops signing |
| `HrCoreActiveRegimeClient.cs:30` | service client | stops signing |
| `HrCoreServiceRegimeClient.cs:30` | service client | stops signing |
| `CandidateDocumentUploadClient.cs:203` | service client | stops signing |
| `WorkflowTriggerService.cs:90` | service client (PLAN-37) | stops signing |

## 0.5 Internal routes and their policy today
| Controller | Route | Policy now | Service only? |
|---|---|---|---|
| Auth.Directory `DirectoryInternalSyncController` | `api/internal/directory` | `ServicePolicies.DirectorySync` | yes |
| File.Storage `DsarInternalController` | `api/internal/dsar-subjects` | `ServicePolicies.DsarSubjectRead` | yes |
| HR.Core `DsarInternalController` | `api/internal/dsar-subjects` | `ServicePolicies.DsarSubjectRead` (also erases: §5) | yes |
| Payroll `DsarInternalController` | `api/internal/dsar-subjects` | `ServicePolicies.DsarSubjectRead` | yes |
| Payroll `PayGradeLookupInternalController` | `api/internal/pay-grades` | `ServicePolicies.PayrollGradeLookup` | yes |
| HR.Core `PoliciesController` | `api/policies` + `api/internal/policies` | `HrCoreReadOnly`, `HrCoreBranchOperation`, `HrCoreWrite`, `HrCoreApprove` | **no** |
| HR.Core `CodeGenerationController` | `api/codes` + `api/internal/codes` | `HrCoreReadOnly`, `HrCoreWrite` | **no** |
| HR.Core `EmployeeLifecycleInternalController` | `api/internal/employees` | `ServicePolicies.HrCoreLookupS2S` (any signed-in user passes) | **no** |
| HR.Core `ServiceRegimeInternalController` | `api/internal/service-regime` | `ServicePolicies.HrCoreLookupS2S` (any signed-in user passes) | **no** |

## Findings that change the implementation
- `AssetClassifier` ignores `Allowed`: it reads `Decision` only, so a failed Enforce call would classify a privileged asset as not privileged. §1 alone would leave that path open; P1 fixes the caller too.
- `ServiceCallerPrincipal.IsServiceAccount` also accepts `preferred_username` starting with `service-account-`. A user with such a name would skip the proof once the middleware trusts it (§3). P3 makes `azp` decide when it is present.
- `AC-09` and `AC-10` post to `api/policies/enforce`, which §2 removes. Both are `required: false` and owned by PLAN-65.
- The CodeQL gate fingerprints each new POST. A service-only POST cannot carry a browser antiforgery token, and the baseline is not to grow. The new internal controllers carry `[ValidateAntiForgeryToken]` on the class and `[IgnoreAntiforgeryToken]` on the bearer-only action. Disclosed in the final report.

## Per-service secret inventory (P-1)
From the code, by one read-only subagent; the working copy is `artifacts/plan42/secret-inventory.md` and it becomes `docs/S2S_SECRET_MATRIX.md` in P4. 15 services, 38 keys in `hr-app-secrets`. Main results:
- No service reads another's secret in code. The exposure is `envFrom`, which puts all 38 keys in every pod (MinIO keys, the Keycloak admin client secret, the broker administrator).
- `RabbitMQ__Username` and `RabbitMQ__Password` are nowhere in `deploy/k8s`: the 14 modules would keep the appsettings placeholder and the guard would refuse them. The per-service secret adds them (the plan asks for "the service's RabbitMQ user").
- The gateway never uses the broker, but `RabbitMQ__Host` in the shared `hr-app-config` makes the guard ask it for credentials. The fix is to take the section out of its environment, not to give it credentials.
- Four keys nothing reads: `POSTGRES_PASSWORD`, `RABBITMQ_DEFAULT_PASS`, `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`, `KEYCLOAK_DIRECTORY_SYNC_SECRET`.
- `hr-migrate` is not a service but reads all 15 connection strings: it needs its own secret.
- `file-storage-web` has no `wiring-patch.yaml`.
- Other k8s gaps, outside this plan's secrets (`DataProtection__KeyRingPath`, `Keycloak__PublicAuthority`, `HrAuth__BehindGateway=false` on the gateway, `Keycloak__RequireHttpsMetadata`): recorded for the final report.

## P-4 placeholders
- `appsettings.json` / `appsettings.Production.json`: 28 values, all `RabbitMQ:Username` and `RabbitMQ:Password` = `USE_ENV_OR_USER_SECRETS`, in the 14 modules. Nothing else, case-insensitive, also inside longer values.
- Server, key names only: all 14 modules have both `RabbitMQ__Username` and `RabbitMQ__Password` set; the gateway has none and has no such section. No container environment value matches a placeholder marker. No Stop.

## The server, read-only, before
- Running tag 3d0b2df84f82, previous f44e52cf58be. 22 containers healthy. Host key matches the plan.
- `.env` key names recorded in `artifacts/plan42/vps/before-state.txt`. `GATEWAY_PROOF_*` absent, as expected.
- `idcard-web-policy-published` exists: 0 messages, 1 consumer.
- `svc-compliance` optional scopes in the realm: hr-core.workflow, hr-core.lookup, file-storage.references, dsar.subject.read.
