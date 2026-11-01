# OPS — Seed inventory (PLAN-38)

Plan: [PLAN-38](acceptance-plans/PLAN-38-ops-first-boot-data-broker-secrets.md) §4 · inventory taken in P0
(`reports/plan38-baseline.md` §0.5).

## The rule (D-38-6)

Seeds run at start-up after the migrations (which `hrm-bootstrap` applies before any application starts). Every service
that seeds reference data goes through `SeedRunner` (`src/Shared/HRManagement.ServiceDefaults/Seeding/SeedRunner.cs`):

| Kind | Call | A failure in Production | Development | Acceptance profile |
|---|---|---|---|---|
| **Mandatory** (national and reference data the service cannot work without) | `RunMandatoryAsync(name, …)` | **the service does not start** (`MandatorySeedFailedException`, Critical log); emergency switch `Seed:AllowMandatoryFailure=true`: Critical log + `seed_mandatory_failures_total`, the start goes on | warning | the original exception fails the start |
| **Optional** (demo and acceptance data, derived data) | `RunOptionalAsync(name, …)` | warning | warning | fails the start |

Messages name the seed and the service, and the cause only as an exception type and SQL state (for example
`SQLSTATE 42501`): an exception message can quote row values, so it is never logged or rethrown.

The profile comes from `SeedProfileResolver` (`Seed:Profile`, `SEED_PROFILE`; default `Demo` in Development, else
`None`). Production runs `None`; the single-host demo runs `Demo` (`docker-compose.vps-demo.yml`).

## Per service

| Service | Seed | None (production) | Demo | Acceptance | Class |
|---|---|---|---|---|---|
| HR.Core | `NationalSeedOrchestrator.ApplyMandatoryAsync` (default tenant, ROOT/HR org units, workflow packs incl. `WorkflowDefinitions`, policy engine, code generation, MDM references, legislation register, service regimes, benefit plans) | **mandatory** | mandatory | mandatory | national / reference |
| HR.Core | `DemoSeedOrchestrator` | — | optional | — | demo |
| HR.Core | `AcceptanceSeedOrchestrator` | — | — | optional (fails the start) | acceptance |
| HR.Core | org-unit paths + org-unit publication (derived, not a seed) | optional | optional | optional | derived |
| HR.Contracts | `ContractsDataSeeder` (the `DEFAULT` contract type that `ContractTypeResolver` falls back to) | **mandatory (new in PLAN-38)** | mandatory | — (the acceptance seeder creates one per agency) | reference |
| HR.Contracts | `DemoContractsSeeder` | — | optional | — | demo |
| HR.Contracts | `AcceptanceContractsSeeder` | — | — | optional (fails the start) | acceptance |
| HR.Training | `ELearningSeeder` + `DemoTrainingSeeder` (sample catalogue) | — | optional | — | demo |
| HR.Training | `AcceptanceTrainingSeeder` | — | — | optional (fails the start) | acceptance |
| Payroll | `SeedPayrollDataAsync` (legal decisions, Law 50/53/Civil regimes, official holidays) | **not seeded** | seeded (no catch) | — | national legal framework → **PLAN-54** |
| Payroll | `DemoPayrollSeeder`, `AcceptancePayrollSeeder` | — | Demo | Acceptance | demo / acceptance |
| Admin.Logistics, HR.Health, HR.Movements, IDCard, IT.Infra, Records.Archive | `Demo*Seeder` / `Acceptance*Seeder` | — | warning on failure | fails the start | demo / acceptance |
| Auth.Directory, Compliance, Dev.Analytics | `Demo*Seeder` / `Acceptance*Seeder` (no catch) | — | failure stops the start | fails the start | demo / acceptance |
| File.Storage | `AcceptanceFileStorageSeeder` (no catch) | — | — | fails the start | acceptance |
| Gateway | none | — | — | — | — |

Seeds inside migrations (whenever a migration applies, i.e. in `hrm-bootstrap`): HR.Core SoD defaults (`HasData`,
`InsertData` into `SodRules`).

## What `None` does not seed although production needs it

1. **HR.Contracts `DEFAULT` contract type** — fixed in PLAN-38: `ContractsDataSeeder` left the `None` condition and runs
   as mandatory in every profile but Acceptance.
2. **Payroll legal framework** (legal decision register, the three regime configurations, official holidays) — seeded
   only in Demo; production provisioning belongs to **PLAN-54** (payroll legal framework in production).

## Least-privilege note

Under the `*_app` roles with enforced row level security (OPS-D, wave 2), a seed writes only rows of the tenant set in
`TenantIdFlow` (the default tenant at start-up). AC-OPS-38h proves the mandatory path on the least-privilege acceptance
stack: the national seed completes as `hr_core_app`, and revoking `INSERT` on `WorkflowDefinitions` makes HR.Core stop
with a message that names the seed.
