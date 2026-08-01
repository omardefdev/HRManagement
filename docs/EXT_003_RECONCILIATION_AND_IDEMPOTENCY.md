# EXT-003 — Reconciliation and exactly-once

## Totals

`reconciliation.totalsMustMatch` and `lineCountMustMatch`:

- Order totalAmount == run net payable
- Order lineCount == disbursement line count
- Channel ack/settle totals must match order

Fixture `totals-mismatch-fail` → FAIL; `recon-totals-pass` → PASS.

## Exactly-once

`exactlyOnce.idempotencyKeyRequired` with `duplicatePolicy=reject`.

Resubmitting the same idempotencyKey with a second business effect fails (`duplicate-submit-fail`). Parallel payroll runs for different runIds with distinct keys pass (`parallel-payroll-pass`).
