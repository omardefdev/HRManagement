# AC-HCM-28 — live run (2026-10-22)

Stack: `hrm-acceptance` after `scripts/acceptance-reset.ps1` + `scripts/acceptance-up.ps1`, built from
`feature/plan-28-committee-appeal-governance` (HEAD `271cbb5`, then `hr-core-web` rebuilt with the PublicCareers route
fix). Gateway `http://localhost:6200`, Keycloak `http://localhost:6388`. `acceptance-up.ps1` ended with the pre-existing
`acceptance-verify.ps1` failure (`"Status" = 1` on a text column, see PLAN-27-EXECUTION); all fourteen services were healthy.
Command: `RUN_ACCEPTANCE=true dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm28`.

The four scenarios share this folder: `steps-*.md` and `http/*.json` are kept per scenario (prefix = scenario tag);
`result.json`, `metadata.json`, `timing.json`, `db-checks.json`, `before.json` and `after.json` hold the last scenario (28b).

| Scenario | AC | Result | Duration | Notes |
|----------|----|--------|----------|-------|
| `28a-c-d` | a, c, d | Pass | 9 s | first run, seeded committee requisition `REQ-2026-A-CMT1` |
| `28h` | h | Pass | 10 s | first run |
| `28e-f-g-i` | e, f, g, i | Pass | 22 s | second run |
| `28b` | b | Pass | 6 s | second run |

First run: `28b` and `28e-f-g-i` failed before any business step — `GET /ui/hr-core/SelectionCommittees?requisitionId=…`
returned 404 because `PublicCareersController.Details` (`[HttpGet("{id}")]`, unconstrained) captured every single-segment
HR.Core URL. Fixed with `{id:guid}`; `hr-core-web` was rebuilt and both scenarios were re-run (their `http/*.json` files are
from the second run; the first `28e` run had already created one requisition on a BR1 position).

Test-only preconditions (documented in each `steps-*.md`): ranks of the appeal requisitions set by SQL (ranking is
AC-HCM-26's scope); `RejectedAt` moved 15 days into the past for AC-HCM-28g.
