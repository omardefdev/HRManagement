# EXT-003 — Payment order binding to approved run

Every payment order submitted to the treasury channel must bind to a payroll run in an allowed status (`Posted` or `Approved`). Orders without `payrollRunId`, or bound to Draft/Calculating/Failed runs, fail validation.

## Required order fields

| Field | Rule |
|---|---|
| orderId | Unique per tenant |
| payrollRunId | Must exist and be Posted/Approved |
| tenantId | SoD scope (IAM-003) |
| totalAmount | Must match run net payable for recon |
| lineCount | Must match disbursement lines |
| idempotencyKey | Required for exactly-once |

## Port join

Lab submit uses [`IPayrollDisbursementGateway`](../src/Shared/HRManagement.Integration.Abstractions/Payroll/IPayrollDisbursementGateway.cs) File/Stub only. Live treasury bind is EXT-003a.

See [ADR-EXT-003](ADR-EXT-003-treasury-payment-reconciliation.md) and fixture `unapproved-run-fail`.

## PLAN-58: orders are made by the posting, sent by the poster, and carried by a bank file

- Orders are made with the batch by the posting (`PaymentBatchIssuer`), one per line with a positive net, with the line's pinned
  account (`BeneficiaryIban`) and an idempotency key `PaymentOrder.IdempotencyKeyFor(run, line)`. A batch made before PLAN-58 gets its
  orders at its first sending, by the same rule.
- Statuses: `Draft` → `Submitted` → `Acknowledged` → `Settled` (or `Rejected`), and `Held` (PLAN-58) for an order kept out of a sending
  with its reason (`HoldReason`: `NO-ACCOUNT`, `ACCOUNT-RETIRED`, `PAYMENT-HOLD`, `REGIME-BLOCKER`, `CRITICAL-ALERT`). `HasLeft` is false
  for `Held`.
- **The bank file** (`PaymentBatchFile`): one per sending (`Initial`, `HeldRelease`) and per approved reissue (`Reissue`), numbered by
  `Sequence` (unique per batch). CSV, UTF-8 without a byte order mark: a header, one row per sent order (`PaymentOrderId,Amount`), and a
  control row `CONTROL,<count>,<total>,<batch>,<sequence>,<kind>`; `ControlCount`/`ControlTotal` stored beside its SHA-256 and content.
  It is stored and read back as it was handed over, never regenerated; a reissue (four eyes) supersedes it with a new sequence.
- **The channel's confirmation is recorded by people** (§7): the poster's acknowledgement (one act, with the channel's reference), and the
  settlement or the rejection, each a request decided by another person (`PayrollDualControlKind.BatchSettle`/`BatchReject`); a settled
  amount that is not the sent orders' sum is 400. A batch thus reaches `Settled`.
