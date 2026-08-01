# EXT-003 — Journey matrix

Source: [`ext-treasury/treasury-payment-matrix.json`](ext-treasury/treasury-payment-matrix.json) — [ADR-EXT-003](ADR-EXT-003-treasury-payment-reconciliation.md).

## ApprovedRunBinding

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RunStatusPolicy | Yes | payroll | approvedRunRequired |
| UnapprovedRunFail | Yes | qa | unapproved-run-fail |

## ExactlyOnce

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| IdempotencyPolicy | Yes | integration | exactlyOnce |
| DuplicateSubmitFail | Yes | qa | duplicate-submit-fail |

## TotalsRecon

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ReconPolicy | Yes | finance | reconciliation |
| TotalsMismatchFail | Yes | qa | totals-mismatch-fail |
| ReconTotalsPass | Yes | qa | recon-totals-pass |

## RejectCorrection

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| RejectPolicy | Yes | integration | rejectCorrectionWorkflow |
| PartialRejectCorrectPass | Yes | qa | partial-reject-correct-pass |

## ManualFileGate

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ManualFilePolicy | Yes | finance | manualFiles |
| ManualUnauditedFail | Yes | qa | manual-file-unaudited-fail |
| PaymentOrderAuditPass | Yes | qa | payment-order-audit-pass |

## CutoffContingency

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CutoffPolicy | Yes | finance | cutoffCalendar |
| CutoffBreachFail | Yes | qa | cutoff-breach-fail |

## ParallelPayroll

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ParallelPayrollPass | Yes | qa | parallel-payroll-pass |

## OutOfOrderAck

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| OutOfOrderPass | Yes | qa | out-of-order-ack-pass |
| DelayAckPass | Yes | qa | delay-ack-pass |

Soft blockers: EXT-004 = NotStarted; treasury-live / bank-sandbox-cert / postal-channel-live = Blocked.
