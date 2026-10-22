# AC-HCM-27 — live run (2026-10-22)

Stack: `hrm-acceptance` after `scripts/acceptance-reset.ps1` + `scripts/acceptance-up.ps1`, built from
`feature/plan-27-offer-merit-reserve`. Gateway `http://localhost:6200`, Keycloak `http://localhost:6388`.
Command: `RUN_ACCEPTANCE=true dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm27` (one scenario per run).

The four scenarios share this folder: `steps-*.md` and `http/*.json` are kept per scenario (prefix = scenario tag);
`result.json`, `timing.json`, `db-checks.json`, `before.json` and `after.json` hold the last scenario (27f-g).

| Scenario | AC | Result | Duration | Correlation id |
|----------|----|--------|----------|----------------|
| `27a-c-d-h` | a, c, d, h | Pass | 15 s | `8c3afdf4-377f-407c-adca-37bd91611da7` |
| `27b` | b | Pass | 11 s | `22eecf9d-560c-4978-809a-0fe53b1df384` |
| `27e` | e | Pass | 1 min 24 s (incl. hr-core restart + first offer-expiry pass) | `7c18fdf5-f7cd-4c5d-abfd-c1062d6a4477` |
| `27f-g` | f, g | Pass | 2 min 23 s (`reserveExpirySeconds` ≈ 125) | `e9dd0e44-09f8-4368-9434-628be2dce0f7` |

Test-only preconditions (documented in each `steps-*.md`): ranks set by SQL (ranking is AC-HCM-26's scope); offer
`ExpiryDate` and `ReserveUntil` moved into the past.
