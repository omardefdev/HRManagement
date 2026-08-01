# EXT-003 — Cutoff and contingency

`cutoffCalendar.cutoffRequired` = true. Submits after the declared cutoff window without contingency hold fail (`cutoff-breach-fail`).

Contingency path: `manual-hold-next-window` — hold the order, do not treat as Settled, open next calendar window submit with a new orderId/idempotencyKey.

Live calendar integration with MOF is deferred to EXT-003a.
