# PAY-004 — Treasury Liquidity, Partial Disbursement & Arrears Ledger

**Card:** PAY-004
**Status:** Done
**Priority:** P1-National / L
**Owner:** Payroll + Treasury + Legal

## Overview

Models the reality of constrained public treasury funding: when a payroll cycle cannot be fully funded, the system supports partial disbursement according to legally authorized priority rules, maintains a per-person arrears ledger, and ensures catch-up payments deduct from the arrears balance without double-payment. The arrears ledger interacts correctly with tax, insurance contributions, currency era transitions, and employee self-service disclosure.

## Arrears Model

### ArrearsLedgerEntry

Each arrears entry is tracked as an `ArrearsLedgerEntry` in `SharedKernel/Payroll/ArrearsLedgerEntry.cs`:

| Field | Type | Description |
|-------|------|-------------|
| `Id` | Guid | Unique entry identifier |
| `EmployeeId` | Guid | Person owed |
| `PayrollRunId` | Guid | Originating payroll run |
| `PeriodStart` | DateOnly | Period the arrears relate to |
| `PeriodEnd` | DateOnly | Period end date |
| `EntitledAmount` | decimal | Full entitled amount for the period |
| `DisbursedAmount` | decimal | Amount actually paid |
| `ArrearsBalance` | decimal | `EntitledAmount - DisbursedAmount` (computed) |
| `CurrencyCode` | string | Currency at time of entitlement |
| `CurrencyEra` | string | New/Old lira era |
| `Reason` | string | Reason code for partial payment |
| `Status` | enum | Open / PartiallySettled / Settled / WrittenOff |
| `CreatedAt` | DateTimeOffset | When the entry was created |
| `SettledAt` | DateTimeOffset? | When fully settled |

### Invariants

- `DisbursedAmount + ArrearsBalance = EntitledAmount` for every entry and every person per cycle
- Partial disbursement never marks the payroll run as fully paid
- Catch-up payment reduces ArrearsBalance; no double-payment even if file re-sent
- Priority rules sourced from signed legal text or authorized decision, not operator configuration
- No manual arrears modification outside audited four-eyes workflow
- Employee sees entitled, disbursed, and arrears balance in ESS-001

## Disbursement Priority

| Priority | Category | Rule Source |
|----------|----------|-------------|
| 1 | Minimum wage guarantee | Law 50/2004 Art. 73 |
| 2 | Pension payments | Decree 135/2026 |
| 3 | Critical agency staff | Authorized agency list |
| 4 | Regular payroll by grade | Treasury circular |

## Tax & Insurance Impact

- Arrears settled in a later period are taxed in the period of settlement
- Insurance contributions calculated on actual disbursement, not entitlement
- Currency era at time of catch-up payment applies (SYR-004)
- Reconciliation report covers three cycles minimum

## Dependencies

PAY-002, PAY-003, EXT-003, EVD-001, SYR-004, SYR-005, SYR-011

## Artifacts

- `SharedKernel/Payroll/ArrearsLedgerEntry.cs`
- `scripts/pay004-validate.ps1`
- `Pay004ArrearsTests`
