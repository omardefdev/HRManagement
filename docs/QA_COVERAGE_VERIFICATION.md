# تحقق تغطية QA-002

## أوامر محلية

```powershell
# وحدة + Coverlet
Remove-Item -Recurse -Force TestResults -ErrorAction SilentlyContinue
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj -c Release `
  --collect:"XPlat Code Coverage" --results-directory TestResults/unit --settings coverlet.runsettings

# بوابة 60% على gateAssemblies + floors
.\scripts\check-coverage.ps1
```

```bash
dotnet test src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj -c Release \
  --collect:"XPlat Code Coverage" --results-directory TestResults/unit --settings coverlet.runsettings
bash scripts/check-coverage.sh
```

عيّنة ناجحة (وحدة فقط، 2026-07-28): **Gate ~81%** (≥ 60%)؛ floors لـ`Payroll.Domain` / `HR.Core.*` / Gateway / ServiceDefaults / Contracts / Compliance ضمن الحدود في [`coverage-floors.json`](../coverage-floors.json).

## Mutation

```powershell
dotnet new tool-manifest --force
dotnet tool install dotnet-stryker --version 4.5.1
dotnet tool run dotnet-stryker -f tests/stryker-payroll.json
```

أو workflow: `.github/workflows/mutation.yml` (`workflow_dispatch` / weekly). العتبة `break: 60`.

## مصفوفة invariants

انظر [QA_RISK_TO_TEST_MATRIX.md](./QA_RISK_TO_TEST_MATRIX.md). رفاق الوحدة: `Qa002InvariantCoverageTests` (INV-02/04/08/13 + PAY-ENG).

## سياسة

- [QA_FLAKY_TEST_POLICY.md](./QA_FLAKY_TEST_POLICY.md)
- [QA_COVERAGE_EXCLUSIONS.md](./QA_COVERAGE_EXCLUSIONS.md)
- [ADR-QA-002-risk-based-coverage.md](./ADR-QA-002-risk-based-coverage.md)

## إغلاق البطاقة

| تحقق | نتيجة |
|---|---|
| مصفوفة P0/P1 pass+fail | ✓ |
| بوابة 60% على gateAssemblies | ✓ |
| floors assemblies | ✓ |
| Stryker config + workflow ≥ 60% break | ✓ |
| flaky + exclusions + CODEOWNERS | ✓ |
| QA-002 Done في backlog | ✓ |
