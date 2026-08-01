# NAT-006 — Witnessed performance run template

**Card:** NAT-006  
**Purpose:** Structured artifact for an independent, witnessed soak/stress/spike run on a production-like environment.  
**Status:** Template only — live certification remains **Blocked** until ORR witnesses a run and signs.

> A completed template is **not** evidence. Evidence requires raw telemetry, reproducible load scripts, and independent witness signatures attached to CI artifacts under `testdata/nat006/out/`.

## Prerequisites

| Item | Required |
|------|----------|
| CAP-001 national envelope targets | Yes |
| Production-like cluster (HA, pooler, OTel) | Yes |
| Load harness (`perf/` scenarios) | Yes |
| Independent witness (not implementer) | Yes |
| Raw results archive (Prometheus/Tempo exports) | Yes |

## Run envelope (from checklist)

| Phase | Duration | Load % | Pass criteria |
|-------|----------|--------|---------------|
| Soak | 72 h | 100 | p95 latency ≤ CAP-001; error rate < 0.1% |
| Stress | 4 h | 150 | No data loss; recovery < 15 min |
| Spike | 15 min | 200 | Headroom ≥ 30%; no corruption |

## Witnessed run record

Fill one JSON file per run: [`nat006-witnessed-run-template.json`](./nat006-witnessed-run-template.json).

### Required fields

- `runId` — unique identifier (UUID)
- `environment` — cluster/site name (not `localhost`)
- `witness.name` / `witness.role` / `witness.signedAt`
- `loadPhases[]` — actual duration, load %, p95, error rate, raw artifact URIs
- `telemetryRefs[]` — Prometheus query snapshots, Tempo trace IDs, Grafana dashboard links
- `resilienceDrills[]` — HA failover, DR RPO/RTO, cyber-RTO (each with raw log refs)
- `headroomPercent` — measured at spike peak
- `zeroLossCorruptionAttested` — boolean + hash of reconciliation report

## CI artifact layout

```
testdata/nat006/out/
  validate-report.json          # scripts/nat006-validate.ps1 (harness gate)
  witnessed-run-<runId>.json    # filled template (manual upload post-witness)
  telemetry-<runId>/            # raw exports (optional tarball)
```

## Validation

```powershell
powershell -File scripts/nat006-validate.ps1
```

The harness gate validates checklist + template presence. It does **not** accept an empty template as live certification — `liveCertificationStatus` stays `Blocked` until witness fields are populated and reviewed outside CI.

## Blockers (live cert)

1. **live-witnessed-performance-run** — independent soak/stress/spike pending ORR
2. **live-dr-cyber-rto-drill** — live DR/cyber-RTO with raw telemetry pending

See also: [NAT_006_PERFORMANCE_CERT.md](../NAT_006_PERFORMANCE_CERT.md), [nat006-checklist.json](./nat006-checklist.json).
