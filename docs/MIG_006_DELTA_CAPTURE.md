# MIG-006 — Delta capture

| Rule | Value |
|---|---|
| noLossRequired | true |
| captureMode | cdc-or-batch-delta |

All changes between freeze and cut must be captured and applied (or explicitly deferred with reason). Lost delta rows → FAIL (`delta-loss-fail`). Clean capture → PASS (`delta-capture-pass`).
