# PLAN-27 — Baseline (session 1, step 0)

**Date:** 2026-10-21
**Branch:** `feature/plan-27-offer-merit-reserve`, created from the tip of `feature/plan-26-assessment-interview-requisition` @ `94c7ce7`.

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 10 warnings (all pre-existing: NU1903 SSH.NET advisories, CS0108, CS8604, CS9113, MVC1004).

## Unit tests

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm02|FullyQualifiedName~Recruitment|FullyQualifiedName~Offer|FullyQualifiedName~SelectionWorkflow|FullyQualifiedName~RequisitionWorkflow|FullyQualifiedName~Fun001|FullyQualifiedName~Syr001"
```

| Result | Count |
|--------|-------|
| Passed | 197 |
| Failed | 2 |
| Total  | 199 |

Pre-existing failures (also listed in the PLAN-26 handoff, not fixed here):

- `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider`
- `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50`
