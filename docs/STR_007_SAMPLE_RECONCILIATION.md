# STR-007 — Sample reconciliation

Verify item #2.

1. Pick KPI current value from measurement sample.
2. Recompute from cited `sourceRef` extract (e.g. GOV-003 open DQ cases; OBS probe counts).
3. PASS when absolute relative difference ≤ tolerance (default 5%).
4. FAIL fixture demonstrates mismatch beyond tolerance.

Artifacts: `sample-reconciliation-pass.json`, `sample-reconciliation-fail.json`.
