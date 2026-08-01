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
