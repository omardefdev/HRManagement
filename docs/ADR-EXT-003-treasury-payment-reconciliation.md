# ADR-EXT-003 — Treasury / payment channel (contract gate pack)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-003 (card 75) |
| Date | 2026-07-30 |
| Dependencies | PAY-003 Satisfied; IAM-003 Satisfied; INT-003 Satisfied; EXT-004 NotStarted (soft) |

## Context

PAY-003 provides resumable payroll runs and PaymentBatch processing. IAM-003 provides SoD/maker-checker. INT-003 coordinates hire/separation/transfer sagas. Platform already has Level-2 `IPayrollDisbursementGateway` with File/Stub adapters writing to `integration-outbox/treasury/`, but there is no signed contract for payment-order binding to approved runs, exactly-once submit, ack/reject/return, bank verify before settle, E2E totals reconciliation, cutoff/contingency, or prohibition of unaudited manual payment files. EXT-004 (live PKI) is not started.

## Decision

| Topic | Choice |
|---|---|
| Matrix | `docs/ext-treasury/treasury-payment-matrix.json` |
| Depth | **Treasury/payment channel contract gate pack** — contracts + mock/sandbox harness; not live MOF/treasury/bank API |
| Port join | `IPayrollDisbursementGateway` File/Stub (no Domain rewrite) |
| Soft blockers | EXT-004; treasury-live; bank-sandbox-cert; postal-channel-live |
| Closure | Done (treasury/payment channel gate pack) |
| Out of scope | Live MOF/treasury/bank/postal APIs; PAY-004 funding shortfall; SYR-011 production channel; Domain rewrite of PaymentBatch line states; EXT-004 live PKI |

## Later stories (not this pack)

| Story | Scope |
|---|---|
| EXT-003a | Live treasury bind |
| EXT-003b | Bank/post adapters |
| EXT-003c | Wire ProcessPaymentBatch → gateway + ack consumer |
| EXT-003d | Join SYR-011 / PAY-004 |
| EXT-003e | EXT-004 signature bind |

## Blocker register

| Id | Status | Note |
|---|---|---|
| PAY-003 | Satisfied | Resumable payroll / batches Done |
| IAM-003 | Satisfied | SoD Done |
| INT-003 | Satisfied | Process sagas Done |
| EXT-004 | NotStarted | Live PKI deferred; lab stub signatures only |
| treasury-live | Blocked | Lab mock only |
| bank-sandbox-cert | Blocked | Stub/file only |
| postal-channel-live | Blocked | Out of scope this pack |

## Consequences

- Payroll export and PAY-004/SYR-011 consume approved-run binding, exactly-once, recon, and reject-correction contracts from this pack.
- Validate fails on unapproved-run orders, duplicate submit without idempotency, totals mismatch as success, unaudited manual files, or wrongly Satisfied soft blockers.
