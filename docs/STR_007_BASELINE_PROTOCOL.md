# STR-007 — Baseline protocol (pre-pilot freeze)

1. For every KPI with `baselineRequired: true`, capture a legacy/current-system value **before** REL-002 pilot Go decision.
2. Record `asOfDate`, `legacySystem`, `sourceTier`, `signedByRole`, and set `prePilotFreeze: true`.
3. After freeze, definition/formula changes require audit waiver (anti-gaming).
4. Numeric `cost_per_employee` may be null until FIN-001; freeze flag still mandatory.
5. Evidence pack: `testdata/str007/baselines/legacy-sample.json` mirrors matrix baselines.

Pilot execution remains REL-002; this pack only gates measurement readiness.
