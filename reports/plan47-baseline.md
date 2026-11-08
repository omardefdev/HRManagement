# PLAN-47 baseline (phase 0)

Taken 2026-11-08 on feature/plan-47-pol-rule-lifecycle at 03cefb23 (origin/main 49a5f08d plus the PLAN-46 merge note and the prompt).

| Step | Command | Result |
|------|---------|--------|
| 0.2 build | `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors, 63 s |
| 0.2 model | `dotnet ef migrations has-pending-model-changes -p src/Services/HRCore/HR.Core.Infrastructure -s src/Services/HRCore/HR.Core.Web` (repository tool manifest) | No changes since the last migration |
| 0.3 unit | `dotnet test src/Tests/HRManagement.UnitTests --filter "PolicyEngine\|PolicyFactsValidator\|PolicyOrchestrator\|Hcm025RecruitmentHireRetired\|Hcm027\|Prod001\|S2s042"` (FullyQualifiedName~) | 353 passed, 0 failed, 0 skipped (131 s with restore) |
| 0.3 integration | `dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm025ProductionApproval\|RecruitmentRequisitionPolicy"` (Testcontainers) | 6 passed, 0 failed (110 s) |
| 0.4 fingerprint | `scripts/pol047-policy-fingerprint.ps1` | Server, read-only: 20 published versions, 37 rules (artifacts/plan47/vps/fingerprint-p0.json, not committed). The acceptance fingerprint is taken when the stack next starts (P5), and AC-POL-47e takes its own "before" inside the test (P-9). |

Deviation from the plan's step 0.4: the fingerprint files go to `artifacts/plan47/`, not to `reports/plan47-fingerprint-before.json` (prompt P-9: ids differ on every stack).
