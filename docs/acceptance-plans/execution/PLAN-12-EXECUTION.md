# PLAN-12 execution — logistics custody (AC-32, AC-33)

## Delivered

### W12.1 — IAM-002 snapshots (Admin.Logistics)
- `EmployeeOrgUnitSnapshots` / `OrgUnitSnapshots` on `AdminLogisticsDbContext`
- Migration `IAM002_AssetLossStateSnapshot`
- Consumers: `EmployeeCreatedConsumer`, `OrgUnitChangedConsumer`; `TransferExecutedConsumer` updates employee snapshot

### W12.2 — Custody org-scope guards (AC-32)
- `AssetOrgScopeGuard` — anchor on open holder; unassigned assets restricted to unrestricted roles
- Wired on `AssetsController` (Index filter, Details, Edit, Assign, Return, MarkLost, Recover, Cancel, Handover PDF)
- `AssetAssignmentsController` Index/Details filtered by employee snapshot

### W12.3 — AC-32 tests + BOLA coverage
- `Ac32LogisticsOrgScopeTests` (integration)
- `LogisticsCustodyScopeTests` (architecture — POST/GET guard assertions)
- `Ac32LogisticsScopeLiveTests` (acceptance)

### W12.4 — Loss before/after snapshots (AC-33)
- `AssetLossReport`: `Before*` / `After*` / settlement fields
- `AssetLossStateCapture`; `AssetLostRules` + `MarkLost` + workflow consumer
- Loss history UI column in `Assets/Details`

### W12.5 — AC-33 tests
- `Ac33AssetLossStateTests` (unit)
- `Ac33AssetLossWorkflowTests` (integration — approve/reject)
- `Ac33AssetLossLiveTests` (acceptance)

### W12.6 — Acceptance seed + script
- `AcceptanceSeedIds.AcceptanceAssetBr1Id` / tag `ACCEPT-ASSET-BR1`
- `AcceptanceLogisticsSeeder` (snapshots + privileged asset + assignment); runs when `RUN_ACCEPTANCE=true`
- `scripts/acceptance-logistics-custody.ps1` → `acceptance-evidence/AC-32`, `AC-33`

## Verify

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Ac33|LogisticsCustody"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Ac32|Ac33"
# After acceptance-up:
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~Ac32|Ac33"
pwsh scripts/acceptance-logistics-custody.ps1
```

## DoD mapping
- AC-32: sibling branch 403 on custody; owner + center OK
- AC-33: structured before/after on loss path; reject leaves After* empty
- CAP-ORG-01 / CAP-LOG-01: org-scope custody + loss settlement snapshots implemented and tested
