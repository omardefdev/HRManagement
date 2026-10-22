# PLAN-30 — Baseline (session 1, step 0)

**Date:** 2026-10-23
**Branch:** `feature/plan-30-candidate-experience`, created from `main` @ `55fbcdd` (contains PLAN-26..29). `testdata` restored with `git checkout -- testdata` first.

## Build

`dotnet build HRManagement.slnx` → **0 errors**, 11 warnings (all pre-existing: CS8604, CS9113, MVC1004, NU1903, xUnit2013).

## Unit tests

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm02|FullyQualifiedName~Recruitment|FullyQualifiedName~Citizen|FullyQualifiedName~PublicCareers|FullyQualifiedName~Ess|FullyQualifiedName~Candidate|FullyQualifiedName~Offer|FullyQualifiedName~Appeal"
```

| Result | Count |
|--------|-------|
| Passed | 551 |
| Failed | 1 |
| Total  | 552 |

Pre-existing failure (also in the PLAN-26..29 baselines, not fixed here):

- `Syr001OfferLegalSourceTests.OfferSubmit_succeeds_with_law50` — expects `Issued` right after submit in Off mode; gets `PendingApproval`.
