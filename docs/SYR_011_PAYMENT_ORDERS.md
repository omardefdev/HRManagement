# SYR-011 — Payment Orders, Bank Channels & Disbursement Reconciliation

**Card:** SYR-011
**Status:** Done
**Priority:** P1-National / L
**Owner:** Payroll + Treasury + Banking Liaison

## Overview

Defines the payment order lifecycle for Syrian public-sector payroll: generation from approved payroll runs, routing through bank/post-office/mobile channels, rejection and return handling, partial settlement, and full reconciliation back to the payroll ledger. Covers IBAN/BBAN validation, channel selection rules, file format specifications, cut-off times, and multi-bank parallel dispatch.

## Payment Channel Model

The machine-readable channel profile is at `docs/syr/payment-channel-profile.json`.

### Supported Channels

| ID | Channel | Format | Settlement |
|----|---------|--------|------------|
| CH-01 | Commercial Bank (CBS) | ISO 20022 pain.001 | T+1 |
| CH-02 | State-Owned Bank | Flat file (national) | T+1 |
| CH-03 | Post Office | Proprietary batch | T+2 |
| CH-04 | Mobile Wallet | API push | Real-time |

### Payment Order States

| State | Description |
|-------|-------------|
| Generated | Created from approved payroll run |
| Validated | IBAN/amount/duplicate checks passed |
| Dispatched | Sent to channel |
| Acknowledged | Channel confirms receipt |
| Settled | Funds confirmed credited |
| Rejected | Channel returned with reason |
| Reconciled | Matched back to payroll ledger |

## Bank Validation Rules

- IBAN format validation per Syrian banking authority specification
- BBAN fallback for legacy accounts with documented migration path
- Duplicate payment detection: same employee + period + amount within 48h window
- Channel-specific amount limits enforced before dispatch
- Cut-off time enforcement per channel with next-business-day rollover

## Reconciliation

- Every dispatched order must reach a terminal state (Settled or Rejected) within SLA
- Unmatched items escalate automatically after configurable timeout
- Reconciliation report per cycle: dispatched vs settled vs rejected vs pending
- Four-eyes approval required to write off or re-dispatch rejected items
- `Dispatched + Pending + Settled + Rejected = Total Payroll Run Amount` invariant enforced

## Security & Compliance

- File signing before dispatch (SIG-001)
- Channel credentials rotated per SEC-004 policy
- All payment files within sovereign boundary (SYR-008)
- Audit trail per EVD-001 evidence model
- No manual payment injection outside approved payroll run

## Dependencies

SYR-004 (currency), SYR-005 (wage), EXT-003 (treasury), PAY-002, EVD-001

## Artifacts

- `docs/syr/payment-channel-profile.json`
- `scripts/syr011-validate.ps1`
- `Syr011PaymentTests`
