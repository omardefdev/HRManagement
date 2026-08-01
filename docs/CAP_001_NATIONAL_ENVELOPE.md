# CAP-001 — National Workload Envelope

Decision context: [ADR-NFR-001-capacity-load.md](ADR-NFR-001-capacity-load.md), [STR_001_WORKLOAD_DATASET.md](STR_001_WORKLOAD_DATASET.md), [NFR_CAPACITY_MODEL.md](NFR_CAPACITY_MODEL.md).

CAP-001 converts STR-001 envelope minima into executable load proof: soak **72h @100%**, stress **4h @150%**, spike **15m @200%**, plus regional mix, payroll window, and failure-during-load scenarios. Target **≥30% headroom** on critical resources at 100% peak.

## Envelope targets

| Indicator | National target |
|---|--:|
| Active+historical persons | 5,000,000 |
| Tenants | ≥ 1,000 |
| Peak HTTP RPS (no static) | 10,000 |
| Payroll window (national) | 5M / 6h |
| Payroll window (Syria interim) | 1.25M / ≤4h |
| Regional mix | 14 governorates |

Test matrix: [`perf/scenarios/cap001-national-envelope.json`](../perf/scenarios/cap001-national-envelope.json).

## k6 scenarios

| Script | Load | Duration (prod) | Lab override |
|---|---|---:|---|
| `cap001-soak-72h.js` | 100% | 72h | `SOAK_DURATION=2h` |
| `cap001-stress-150.js` | 150% | 4h | `STRESS_DURATION=10m` |
| `cap001-spike-200.js` | 200% | 15m | `SPIKE_DURATION=3m` |
| `cap001-failure-during-load.js` | 100% + chaos | 30m | `FAILURE_LOAD_DURATION=5m` |

Shared helpers: [`perf/k6/lib/cap001.js`](../perf/k6/lib/cap001.js) (extends `gateway-mixed` paths + regional tags).

## Run commands

```powershell
# Lab smoke (short soak)
$env:SOAK_DURATION = "2h"
$env:VUS_BASE = "50"
$env:ACCESS_TOKEN = "<jwt>"
docker compose -f perf/docker-compose.cap001.yml --profile cap001 run --rm k6-cap001-soak

# Stress / spike
docker compose -f perf/docker-compose.cap001.yml --profile cap001 run --rm k6-cap001-stress
docker compose -f perf/docker-compose.cap001.yml --profile cap001 run --rm k6-cap001-spike

# Failure during load (pair with chaos)
$env:FAILURE_SCENARIO = "db_latency"
docker compose -f perf/docker-compose.cap001.yml --profile cap001 run --rm k6-cap001-failure

# Headroom report from k6 JSON
pwsh scripts/cap001-headroom-calculator.ps1 -K6Summary perf/results/<run>/summary.json -ScenarioId soak-100
```

Validate harness:

```powershell
pwsh scripts/cap001-envelope-validate.ps1
```

## SLO acceptance

| Scenario | Failed rate | P95 latency | Other |
|---|---:|---:|---|
| soak @100% | &lt; 1% | &lt; 2000 ms | No data loss/duplication |
| stress @150% | &lt; 5% | &lt; 4000 ms | Safe degradation only |
| spike @200% | &lt; 5% | &lt; 4000 ms | Recovery ≤ 15m |
| failure-during-load | &lt; 5% | — | Explicit errors; zero silent loss |
| payroll window | — | — | Fail ratio &lt; 1%; outbox within J3 |

Results schema: [`perf/cap001-results-schema.json`](../perf/cap001-results-schema.json).

## Headroom (30%)

Headroom is computed by `scripts/cap001-headroom-calculator.ps1`:

1. **Preferred:** Prometheus/Grafana snapshot (`cpu_utilization_pct`, `memory_utilization_pct`, `db_connections_pct`) → `headroom = 100 - peak_util`.
2. **Fallback:** p95 latency margin vs SLO ceiling.
3. **Estimate:** linear VU scaling when only k6 summary is available.

Pass requires `computedPct ≥ 30` **and** SLO checks pass.

## Resource limits (compose)

[`perf/docker-compose.cap001.yml`](../perf/docker-compose.cap001.yml) extends NFR load compose with documented CPU/memory ceilings for gateway, HR.Core, Payroll, Postgres, and RabbitMQ. Production national runs use the same topology with production limits — no special tuning that violates production config.

## Failure injection tags

`cap001-failure-during-load.js` tags requests with `failure_injection`:

| Tag | Pair with |
|---|---|
| `db_latency` | `docker-compose.chaos.yml` / `nfr-chaos-inject.ps1` |
| `gateway_503` | upstream fault injection |
| `mq_backlog` | slow consumer / queue pressure |
| `payroll_worker_loss` | worker scale-down during payroll poll |

## Evidence pack

- Matrix + k6 harness + schema + validate script
- `Cap001EnvelopeTests` (CI)
- Signed raw results under `perf/results/cap001-<stamp>/` with `cap001-results.json`
- Independent performance run on production-like environment (outside repo closure gate)

## Separation from NFR-001 lab

NFR-001 proves methodology at `scale_factor=0.002` (10k employees, 2h soak). CAP-001 reuses the same scripts and seed shape at national scale with full duration envelopes.
