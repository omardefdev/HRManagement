# SYR-004 — New Syrian Lira Currency Conversion

## Purpose

Handle the transition from old Syrian Lira to new Syrian Lira at a fixed 100:1 conversion ratio, ensuring no data loss, no double conversion, and full reconciliation of all financial amounts across eras.

## Scope

- Currency code and era tracking for every monetary amount
- Fixed 100:1 conversion (old → new)
- Official rounding policy (banker's rounding to 2 decimal places)
- Dual display for historical periods
- Contract/debt/aggregate conversion
- Import/export validation (untagged amounts must fail safely)
- Payslip and report era-awareness
- Database and interface precision (no float; decimal only)

## Domain Model

### SyrianCurrencyConverter (Static Service)

Provides conversion and normalization logic.

```
SyrianCurrencyConverter
├── ConversionRatio = 100
├── OldCurrencyCode = "SYP"
├── NewCurrencyCode = "SYP-NEW"
├── CutoverDate = 2026-07-31T00:00:00Z
├── ToNewLira(oldAmount) → decimal
├── ToOldLira(newAmount) → decimal
├── GetEra(date) → CurrencyEra
├── Normalize(amount, sourceEra, targetEra) → decimal
├── RoundOfficial(amount) → decimal
```

### CurrencyEra Enum

```
CurrencyEra
├── OldLira = 0
├── NewLira = 1
```

### Configuration: `docs/syr/currency-era.json`

Contains official conversion parameters, rounding rules, reconciliation requirements, and legal source reference.

## Business Rules

- Every amount specifies currency and era; no implicit conversion
- No double conversion (convert exactly once)
- 1 new lira = 100 old lira, lossless with decimal arithmetic
- Balances before/after cutover must reconcile exactly
- From 2026-07-31 the official default is new lira; historical data remains viewable
- No floating-point arithmetic for monetary values
- Rounding policy sourced from certified instrument
- Import files without era tag must fail safely (not silently assume)

## Acceptance Criteria Mapping

- [x] Every amount identifies currency and era — `CurrencyEra` enum
- [x] No double conversion — `Normalize` is idempotent when source == target
- [x] 1 new = 100 old without loss — decimal arithmetic, `ConversionRatio = 100`
- [x] Balances reconcile — property test: `ToNewLira(x) * 100 == x`
- [x] Official default after cutover — `GetEra(date)` returns `NewLira` from 2026-07-31
- [x] No float — all amounts are `decimal`
- [x] Rounding from certified source — `RoundOfficial` uses banker's rounding

## Artifacts

| File | Purpose |
|------|---------|
| `docs/SYR_004_CURRENCY.md` | This document |
| `docs/syr/currency-era.json` | Era configuration and rules |
| `SharedKernel/Currency/SyrianCurrencyConverter.cs` | Conversion logic |
| `scripts/syr004-validate.ps1` | Validates SYR-004 artifacts |
| `Syr004CurrencyTests.cs` | Unit tests |

## Validation

- `scripts/syr004-validate.ps1`
- `Syr004CurrencyTests`
