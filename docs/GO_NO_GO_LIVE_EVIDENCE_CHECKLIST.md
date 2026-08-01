# Go/No-Go — Live evidence checklist (anti-template)

**Purpose:** Final production gate checklist that **rejects template-only evidence**.  
**Enforcement:** [`scripts/go-no-go-gate.sh`](../scripts/go-no-go-gate.sh) exits 1 when any blocking row is NOT-CLOSED.

## Core rule

> A document, template, policy JSON, or unsigned checklist row is **never** evidence.  
> Every blocking condition must be satisfied by **observable live artifacts** or marked **NOT-CLOSED**.

This checklist extends [11-NATIONAL-GO-NO-GO-CHECKLIST.md](./completion-backlog/11-NATIONAL-GO-NO-GO-CHECKLIST.md) with explicit rejection criteria.

## Rejected evidence (automatic NO-GO)

| Pattern | Example | Verdict |
|---------|---------|---------|
| Empty template JSON | `nat006-witnessed-run-template.json` with blank `witness` | **REJECT** |
| Unsigned decision JSON | `docs/nat/go-no-go-decision.json` without signatures | **REJECT** |
| Config-only external claim | `ExtTreasury:LiveTreasuryClaim=true` without MOU | **REJECT** |
| Checklist row with no artifact URI | NAT row marked Done without `evidenceUri` | **REJECT** |
| Synthetic CI timestamps only | `rel003-incident-game-day.ps1` without live page proof | **REJECT** (pilot) |
| `ApprovedInterim` without closure path | `register.json` decisions without `repoClosureStatus` | **REJECT** for national go-live |

## Required live evidence categories

### Build & test (CI-proven)

- [ ] Release build 0 errors (`GATE_BUILD_GREEN=true`)
- [ ] Full test matrix green, zero mandatory skips (`GATE_TESTS_GREEN=true`)
- [ ] EF migrations discoverable (`scripts/check-migration-discovery.sh`)

### Performance (NAT-006)

- [ ] Witnessed run JSON with populated `witness.signedAt` and `telemetryRefs`
- [ ] Raw soak/stress/spike telemetry archives
- [ ] Headroom ≥ 30% measured at spike peak
- [ ] HA/DR/cyber-RTO drills with raw logs

### External channels (cannot be inferred from repo)

- [ ] Live treasury / MOF settlement (EXT-003)
- [ ] Civil registry MOI verification (EXT-001)
- [ ] National PKI signing (EXT-004)

### Governance (SYD-001..018)

- [ ] SYD tracker status ≠ NOT-CLOSED for blocking rows
- [ ] Legal register interim decisions closed or explicitly waived with sign-off
- [ ] 3 signed shadow payroll cycles (national envelope)

### Operations

- [ ] On-call roster with named primary/backup and page channel ([REL_003](./REL_003_ONCALL_ROSTER.md))
- [ ] Incident game-day with measured MTTA (live or signed waiver)

## Gate commands

```bash
# Repo-side structural validation
powershell -File scripts/nat006-validate.ps1
powershell -File scripts/gov002-validate.ps1

# Final live-evidence gate (exits 1 on NOT-CLOSED)
./scripts/go-no-go-gate.sh
```

## Current status

**Verdict: NO-GO** — template artifacts exist; live witnessed runs and external channel MOUs remain NOT-CLOSED.

See: [SYD-001-018-TRACKER.md](./syd/SYD-001-018-TRACKER.md), [REMAINING_IMPLEMENTATION.md](./REMAINING_IMPLEMENTATION.md).
