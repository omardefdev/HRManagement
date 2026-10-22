# PLAN-29 — Baseline (session 1, step 0)

**Date:** 2026-10-22
**Branch:** `feature/plan-29-hire-contract-documents`, created from `main` @ `e9a240a` (contains PLAN-26/27/28). `testdata` restored with `git checkout -- testdata` first.

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 10 warnings (all pre-existing: CS8604, CS9113, MVC1004, NU1903).

## Unit tests

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm02|FullyQualifiedName~Recruitment|FullyQualifiedName~Hire|FullyQualifiedName~Fun001|FullyQualifiedName~Offer|FullyQualifiedName~Syr001|FullyQualifiedName~Syr003|FullyQualifiedName~MessageContract"
```

| Result | Count |
|--------|-------|
| Passed | 423 |
| Failed | 2 |
| Total  | 425 |

Pre-existing failures (also in the PLAN-26/27/28 baselines, not fixed here):

- `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider` — `HRCoreDbContext` not registered in the test's service collection.
- `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50` — expects `Issued` right after submit in Off mode; gets `PendingApproval`.
