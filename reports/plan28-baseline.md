# PLAN-28 — Baseline (session 1, step 0)

**Date:** 2026-10-22
**Branch:** `feature/plan-28-committee-appeal-governance`, created from the tip of `feature/plan-27-offer-merit-reserve` @ `d8f6714` (nothing merged to `main`).

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 10 warnings (all pre-existing: NU1903 SSH.NET advisories, CS0108, CS8604, CS9113, MVC1004).

## Unit tests

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm02|FullyQualifiedName~Recruitment|FullyQualifiedName~Offer|FullyQualifiedName~Selection|FullyQualifiedName~Committee|FullyQualifiedName~Appeal|FullyQualifiedName~Fun001|FullyQualifiedName~Syr001"
```

| Result | Count |
|--------|-------|
| Passed | 281 |
| Failed | 2 |
| Total  | 283 |

Pre-existing failures (listed in the PLAN-26 and PLAN-27 handoffs, not fixed here):

- `Syr001ProductionBindingTests.AddHrLegislationRegister_resolves_provider`
- `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50` (expects `Issued` right after submit in Off mode; gets `PendingApproval`)
