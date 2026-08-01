# NFR-001 k6 harness

See [NFR_LOAD_TOPOLOGY.md](../docs/NFR_LOAD_TOPOLOGY.md) and [ADR-NFR-001-capacity-load.md](../docs/ADR-NFR-001-capacity-load.md).

## Scripts

| File | Purpose |
|---|---|
| `smoke.js` | Ready probe / CI |
| `gateway-mixed.js` | J1/J2 mixed paths |
| `lookup-list.js` | PERF-002 lookups/lists |
| `payroll-run.js` | PERF-001 / J4 (needs `PAYROLL_RUN_ID`) |
| `export-async.js` | PERF-004 export path |
| `org-tree.js` | PERF-003 tree |
| `lib/auth.js` | `BASE_URL` + Bearer |
| `lib/thresholds.js` | OBS-002-aligned thresholds + stages |
| `profiles/*.json` | Documentation of profile intent |

## Run

```powershell
pwsh scripts/nfr-k6-run.ps1 -Scenario smoke -Profile smoke
pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile load -Docker
```
