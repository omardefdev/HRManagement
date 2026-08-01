# PROD-001 — Journey matrix

All steps mandatory. Evidence via matrix flags + `testdata/prod001` fixtures.

| Journey | Evidence |
|---|---|
| VersionedChange | law-change-pass / unversioned-template-fail |
| HistoricalPin | historical-pin-pass / historical-drift-fail |
| SimulationGate | synthetic-sim-pass / real-data-sim-fail |
| PublishSafety | invalid-publish-fail / same-role-publish-fail / arbitrary-code-fail |
| OverrideRollback | override-bypass-national-fail / agency-override-within-ceiling-pass / rollback-pass |
