# SEED-001 — Master Seed Data (ONB-Ready)

National mandatory seed packs, agency overlays via ONB-001 manifest, and development demo layer.

## Pack registry

| Pack ID | Purpose |
|---------|---------|
| `NATIONAL-MANDATORY-v1` | Platform bootstrap + workflows + policies + code + MDM |
| `NATIONAL-MDM-SY-v1` | Syrian national reference catalogs (12 kinds) |
| `NATIONAL-WF-v1` | Nine national workflow definitions |
| `NATIONAL-COD-v1` | Code generation national policies |
| `NATIONAL-POLICY-v1` | Policy engine national domains |

Constants: `src/Shared/HRManagement.SharedKernel/Seeding/SeedPackIds.cs`

## Orchestration

`NationalSeedOrchestrator.ApplyMandatoryAsync` runs at HR.Core startup in this order:

1. `HRCoreDataSeeder` — tenant, shifts, ROOT org
2. `WorkflowPackSeeder` — all workflow definitions
3. `PolicyEngineSeeder` + `PolicyEngineDomainSeeds`
4. `CodeGenerationSeeder`
5. `MdmReferenceSeeder` — from `seed-packs/national-mdm-sy-v1/catalog.json`
6. Optional: `CodSequenceBootstrapService` when `Seed:BootstrapCounters=true`

Development only: `DemoSeedOrchestrator` → `DemoDataSeeder`.

## Idempotency

`SeedPackApplication` records `(TenantId, PackId, ReleaseDigest)` to prevent duplicate full applies.
Incremental patches (workflows, bindings, MDM entries, policy rules) upsert missing rows on every startup.

## Agency onboarding (ONB-001)

```
POST /api/onboarding/agencies/apply?dryRun=true
```

Requires `HrCoreWrite` + `HrCoreApprove` (four-eyes).

Pipeline:

1. Validate manifest (`AgencyManifestLoader`)
2. Ensure national mandatory packs
3. Create/reuse tenant
4. Copy MDM `referenceSubset` from national tenant
5. Apply policy/code overlay packs from `seed-packs/policy-overlays/` and `seed-packs/code-overlays/`
6. Record `SeedPackApplication`

Example manifest: `seed-packs/examples/agency-ministry-finance.json`

CLI: `scripts/onb001-apply-manifest.ps1 -Manifest path [-DryRun]`

## Configuration

| Key | Default | Description |
|-----|---------|-------------|
| `Seed:IncludeSamplePosting` | `false` | Demo public job posting (dev: `true`) |
| `Seed:BootstrapCounters` | `false` | Run COD sequence bootstrap after seed |

## Validation

```powershell
./scripts/seed-001-validate.ps1
./scripts/mdm001-reference-validate.ps1
./scripts/onb001-validate.ps1
dotnet test --filter "FullyQualifiedName~NationalSeedTests|FullyQualifiedName~AgencyProvisioningTests"
```

## Directory layout

```
seed-packs/
  national-mdm-sy-v1/catalog.json
  policy-overlays/
  code-overlays/
  examples/agency-ministry-finance.json
```
