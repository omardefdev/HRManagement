# PLAN-38 baseline (P0)

Plan: [PLAN-38](../docs/acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md) · progress: [plan38-progress.md](plan38-progress.md).

## 0.2 Build

`dotnet build HRManagement.slnx -warnaserror`: 0 warnings, 0 errors (21 s, incremental).

## 0.3 PLAN-36 artifacts

| Artifact | Present |
|---|---|
| `docker-compose.vps.yml` | yes |
| `ProductionConfigurationGuard` (`src/Shared/HRManagement.ServiceDefaults/ProductionConfigurationGuard.cs`) | yes |
| `scripts/vps/deploy.ps1` | yes |
| also used here: `deploy/vps/remote-deploy.sh`, `deploy/vps/build-on-host.sh`, `scripts/vps/build-on-host.ps1`, `scripts/vps/lint-compose.ps1` | yes |

## 0.4 RabbitMQ topology

Code-side inventory (MassTransit 8.5.10, `Directory.Packages.props`): see the live listing and the classification in
[plan38-rabbit-topology.json](plan38-rabbit-topology.json). Facts from the code:

- Endpoint prefix: `MassTransit:EndpointPrefix`, else the entry assembly name lowercased with non-alphanumerics turned
  into `-` (`MassTransitExtensions.ResolveEndpointPrefix`). Not set anywhere, so `HR.Core.Web` → `hr-core-web`. Every
  queue (consumers and sagas) is `<prefix>-<kebab name>`; no explicit `ReceiveEndpoint`, no entity name formatter.
- Exchanges: one per published or consumed type, `Namespace:TypeName` (all under `HRManagement.MessageContracts.Events`
  or `.Commands`), plus one exchange per receive endpoint with the queue's name, plus `<queue>_error` / `<queue>_skipped`
  (exchange and queue) on the first fault / skip.
- Exceptions outside `HRManagement.`: `HR.Contracts.Infrastructure.Sagas:ISeparationSagaFinalized` (published and consumed
  by HR.Contracts); `MassTransit:Fault` and `MassTransit:Fault--<Namespace>:<Type>--` (every service with consumers),
  including `MassTransit:Fault--HR.Contracts.Infrastructure.Sagas:*TimeoutMsg--` and the HR.Movements equivalents.
- Delayed scheduler (`UseDelayedMessageScheduler`): saga `Schedule(...)` declares `<saga queue>_delay` of type
  `x-delayed-message` (`x-delayed-type=fanout`), always under the service prefix: HR.Contracts (separation, hire case) and
  HR.Movements (transfer, secondment). Needs the `rabbitmq_delayed_message_exchange` plugin (absent today).
- Bus temporary queue `{MachineName}_{ProcessName}_bus_{NewId}`: not prefixed; no request client is used, kept as a margin.
- Gateway: no bus at all (`mq_gateway` in the production file is unused).
- File.Storage: a bus without consumers (connects, declares nothing it owns).
- Existing `scripts/init-rabbitmq-service-users.sh` / `docker/rabbitmq/definitions-service-users.json` are wired nowhere
  and would refuse every `HRManagement.MessageContracts.*` declare (and the JSON every `MassTransit:Fault*`).

## 0.5 Seed inventory (start-up)

All under `src/`. Profile from `SeedProfileResolver.Resolve` (`Seed:Profile`, `SEED_PROFILE`, legacy `Seed:Acceptance`;
default Demo in Development, else **None**). Enum: `None, Demo, Acceptance`.

| Service | Seed | Invoked at | Profiles | In None? | Failure handling | Class |
|---|---|---|---|---|---|---|
| Gateway | none | — | — | — | — | — |
| Admin.Logistics | Acceptance/DemoLogisticsSeeder | Admin.Logistics.Web/Program.cs:61/63 | Acc / Demo | no | catch `when != Acceptance` :76 → Warning | acceptance / demo |
| Auth.Directory | Acceptance/DemoAuthSeeder | Auth.Directory.Web/Program.cs:62/64 | Acc / Demo | no | no catch (crash) | acceptance / demo |
| Compliance | Acceptance/DemoComplianceSeeder | Compliance.Web/Program.cs:71/73 | Acc / Demo | no | no catch (crash) | acceptance / demo |
| Dev.Analytics | Acceptance/DemoAnalyticsSeeder | Dev.Analytics.Web/Program.cs:63/65 | Acc / Demo | no | no catch (crash) | acceptance / demo |
| File.Storage | AcceptanceFileStorageSeeder | File.Storage.Web/Program.cs:58 | Acc | no | no catch (crash) | acceptance |
| HR.Contracts | AcceptanceContractsSeeder | HR.Contracts.Web/Program.cs:104 | Acc | no | catch `when != Acceptance` :117 → Warning | acceptance |
| HR.Contracts | **ContractsDataSeeder** (`DEFAULT` contract type) | HR.Contracts.Web/Program.cs:107 | **Demo only** | **no** | same catch, Warning (swallowed) | **reference** (gated as demo) |
| HR.Contracts | DemoContractsSeeder | HR.Contracts.Web/Program.cs:108 | Demo (+ per agency in Acc) | no | same catch, Warning | demo |
| HR.Core | **NationalSeedOrchestrator.ApplyMandatoryAsync** (incl. WorkflowPackSeeder, WorkflowPlan03Seeder → `WorkflowDefinitions`) | HR.Core.Web/Program.cs:295 | **all** | **yes** | catch `when != Acceptance` :317 → **Warning "HR.Core seed skipped."** (swallowed in Production) | **national / reference** |
| HR.Core | DemoSeedOrchestrator | HR.Core.Web/Program.cs:300 | Demo | no | catch :302 → Warning | demo |
| HR.Core | AcceptanceSeedOrchestrator | HR.Core.Web/Program.cs:308 | Acc | no | rethrown | acceptance |
| HR.Core | OrgUnitPath recompute + org-unit publish (derived, not a seed) | HR.Core.Web/Program.cs:309-310 | all | yes | same catch :317, Warning | derived |
| HR.Health | Acceptance/DemoHealthSeeder | HR.Health.Web/Program.cs:63/65 | Acc / Demo | no | catch :72 → Warning | acceptance / demo |
| HR.Movements | Acceptance/DemoMovementsSeeder | HR.Movements.Web/Program.cs:86/88 | Acc / Demo | no | catch :90 → Warning | acceptance / demo |
| HR.Training | AcceptanceTrainingSeeder | HR.Training.Web/Program.cs:57 | Acc | no | catch :65 → Warning | acceptance |
| HR.Training | ELearningSeeder + DemoTrainingSeeder | HR.Training.Web/Program.cs:60/61 | Demo | no | catch :65 → Warning | demo (sample catalogue) |
| IDCard | Acceptance/DemoIDCardSeeder | IDCard.Web/Program.cs:64/66 | Acc / Demo | no | catch :68 → Warning | acceptance / demo |
| IT.Infra | Acceptance/DemoITSeeder | IT.Infra.Web/Program.cs:61/63 | Acc / Demo | no | catch :66 → Warning | acceptance / demo |
| Payroll | **SeedPayrollDataAsync** (legal decisions, Law50/53/Civil regimes, holidays) | Payroll.Web/Program.cs:136 | **Demo only** | **no** | no catch (crash) | **national legal framework** (gated as demo) → PLAN-54 |
| Payroll | DemoPayrollSeeder | Payroll.Web/Program.cs:139 | Demo | no | no catch | demo |
| Payroll | AcceptancePayrollSeeder | Payroll.Web/Program.cs:145 | Acc | no | no catch | acceptance |
| Records.Archive | Acceptance/DemoArchiveSeeder | Records.Archive.Web/Program.cs:62/64 | Acc / Demo | no | catch :67 → Warning | acceptance / demo |

In migrations (whenever migrations run): HR.Core `HasData` SoD defaults (`HRCoreDbContext.cs:271,277`) and `InsertData`
into `SodRules`. `TryMigrateAsync` logs Critical and rethrows in Production on a migration failure.

Writers of HR.Core `WorkflowDefinitions`: `WorkflowPackSeeder` (insert, and `RetireAsync` updates) and
`WorkflowPlan03Seeder`, both from `NationalSeedOrchestrator`, on first apply **and** on every start-up through
`ApplyIncrementalPatchesAsync` (idempotent: insert only when missing).

No shared mandatory-seed helper exists; `ProductionConfigurationGuard.cs:161-176` refuses `Deployment:Mode=demo` with
profile None.

### Live listing (0.4)

Acceptance stack `hrm-acceptance` (lab profile, administrator user `hrm_ci`, images from PLAN-37 P6), restarted and
driven through the AC-HCM-33 journey (`RecruitmentToHireJourneyE2ETests`, Pass, 20 s). Listing after the journey:
200 exchanges, 122 queues (all quorum), **0 unclassified** (classification script output:
[plan38-rabbit-topology.json](plan38-rabbit-topology.json)).

| Kind | Count | Owner rule |
|---|---|---|
| endpoint (queue + same-name exchange) | 240 | the service prefix `<svc>-web-` |
| message-type `HRManagement.MessageContracts.*` | 67 | shared (consumers recorded as `boundTo`) |
| `HR.Contracts.Infrastructure.Sagas:ISeparationSagaFinalized` | 1 | hr-contracts-web |
| `MassTransit:Fault`, `MassTransit:Fault--…--` | 3 | shared |
| `_error` (exchange + queue) | 4 | the service prefix |
| `amq.*`, default | 7 | broker |
| `_delay`, `_bus_` | 0 | not seen live (no plugin, no request client); code-generated names in the JSON |

## 0.6 Unit baseline

`dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Sec004|FullyQualifiedName~Ten002|FullyQualifiedName~DB001"`:
22 / 22 passed.

## Server before-picture (read-only, 2026-11-01T10:3xZ)

- Release `9d457dab36d7` (previous `00926107a37b`), `HRM_DEPLOYMENT_MODE=demo`.
- `/opt/hrm/shared/.env`: mode 600, owner `hrm`; 38 keys (names only): APP_DOMAIN SSO_DOMAIN ACME_EMAIL HRM_ACME_CA
  ADMIN_ALLOWED_CIDRS HRM_IMAGE_REPO IMAGE_TAG HRM_DEPLOYMENT_MODE COMPOSE_PROFILES HRM_ORGANIZATION_NAME HRM_FILE_PROVIDER
  HRM_OTEL_ENABLED HRM_REQUIRE_LICENSE POSTGRES_USER RABBITMQ_DEFAULT_USER KEYCLOAK_ADMIN POSTGRES_PASSWORD
  RABBITMQ_DEFAULT_PASS KEYCLOAK_ADMIN_PASSWORD KEYCLOAK_HR_GATEWAY_CLIENT_SECRET KEYCLOAK_DIRECTORY_SYNC_SECRET
  HR_CORE_OTP_HMAC_KEY GATEWAY_CALLER_PROOF_SECRET DEMO_USERS_PASSWORD and the 14 `SVC_*_CLIENT_SECRET`.
  No `POSTGRES_<SVC>_*_PASSWORD`, `RABBITMQ_<SVC>_PASS` or `POSTGRES_BACKUP_PASSWORD` yet.
- Databases: the 16 (`keycloak_db`, `gateway_audit_db` and 14 service databases) plus `hrm_admin` and `postgres`.
  Roles: the 15 `*_app` / `*_migrator` pairs exist (created by `init-service-roles.sh` at first init, one shared
  password), plus the administrator `hrm_admin`; no `hrm_backup`.
- RabbitMQ: 121 queues, **0 messages in total**; 14 connections, all as the administrator `hrm_admin`; node data on an
  **anonymous** volume (`/var/lib/rabbitmq`), random hostname (container id) — data would not survive a recreate.
- Named volumes (size): postgres_data 283 MB, seq_data 784 MB, clamav_data 108 MB, file_storage_data 88 KB,
  14 `dp_*` + gateway_dataprotection_keys 8 KB each, two integration outboxes 4 KB, caddy_data 164 KB, caddy_config 12 KB.
- Disk `/` 145 GB, 28 GB used; memory 23 GiB, 17 GiB available. No `/opt/hrm/backups` yet.
