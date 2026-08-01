# MIG-001 — Profiling protocol

1. Migration Office runs sample extract or count query per Inventoried source.
2. Record completeness, nullRate, duplicateKeyRate, measuredAt, method.
3. Agency HR/Payroll (or national owner for civil API) signs the profiling report (`agencySigned: true`).
4. Evidence: `testdata/mig001/profiling/agency-signed-profile.json`.
5. Unsigned or missing signature → validate FAIL.
6. Lab/synthetic volumes are acceptable for pack closure while **agency-extract-access** remains Blocked, provided `estimateOnly: false` and method/measuredAt are set.
