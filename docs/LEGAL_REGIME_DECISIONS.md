# Legal Regime Decisions — Configurable Model

**Version:** 1.1 (GOV-002)  
**Date:** 2026-07-28

Maps payroll configuration fields to **legal decisions** in [`legal-decisions/register.json`](./legal-decisions/register.json). See [GOV_002_LEGAL_DECISION_REGISTER.md](./GOV_002_LEGAL_DECISION_REGISTER.md).

## Supported regime codes

| Code | Name (AR) | Default active | GOV-002 status |
|------|-----------|----------------|----------------|
| `LAW_50_2004` | قانون العاملين الأساسي 50/2004 | Yes | ApprovedInterim (platform) |
| `CIVIL_SERVICE_2026` | قانون الخدمة المدنية الجديد | No | PendingEnactment |

## Decision matrix

| # | Decision item | Configuration field | LAW_50_2004 | CIVIL_SERVICE_2026 | decisionId |
|---|---------------|----------------------|-------------|--------------------|------------|
| 1 | Income tax brackets | `TaxBrackets` | Progressive 0–20% | PendingEnactment | LD-TAX-BRACKETS |
| 2 | Tax exemption | `TaxExemptionAmount` | 0 | PendingEnactment | LD-TAX-EXEMPTION |
| 3 | Taxable base | `TaxableBaseMode` | `GrossPay` | PendingEnactment | LD-TAXABLE-BASE |
| 4 | EOS first-period rate | `EosGratuityRateFirstPeriod` | 0.5 | PendingEnactment | LD-EOS-FORMULA |
| 5 | EOS threshold years | `EosGratuityFirstYearsThreshold` | 5 | PendingEnactment | LD-EOS-FORMULA |
| 6 | EOS after-threshold rate | `EosGratuityRateAfterThreshold` | 1.0 | PendingEnactment | LD-EOS-FORMULA |
| 7 | Daily rate divisor | `DailyRateDivisor` | 30 | PendingEnactment | LD-DAILY-DIVISOR |
| 8 | Proration basis | `ProrationDaysBasis` | WorkingDays | PendingEnactment | LD-PRORATION-BASIS |
| 9 | Non-working weekdays | `NonWorkingDays` | Friday,Saturday | PendingEnactment | LD-NON-WORKING-DAYS |
| 10 | Official holidays | `OfficialHoliday` rows | Syrian fixed set | PendingEnactment | LD-OFFICIAL-HOLIDAYS |
| 11 | Mission allowance | `MissionAllowanceAmount` | 0 | PendingEnactment | LD-MISSION-ALLOWANCE |
| 12 | Secondment percent | `SecondmentAllowancePercent` | 0 | PendingEnactment | LD-SECONDMENT-PERCENT |
| 13 | Training premium | `EnableTrainingPremiums` / amount | false / 0 | PendingEnactment | LD-TRAINING-PREMIUM |
| 14 | Leave payout divisor | `LeavePayoutDailyDivisor` | 30 | PendingEnactment | LD-LEAVE-PAYOUT-DIVISOR |
| 15 | Retro correction limit | `MaxRetroCorrectionMonths` | 12 | PendingEnactment | LD-RETRO-MONTHS |

## Activation procedure

1. Seed both regimes at first startup (`Payroll.Web/Program.cs`).
2. LegalAttest → Publish for any new version (GOV-002).
3. Set `IsActive = true` on exactly one published `PayrollConfiguration`.
4. When civil service law is enacted, update decisions to ApprovedInterim/gazette status, then Attest+Publish.

## Audit trail

LegalAttestation + PayrollConfigChangeNotice + PayrollAuditRecord / ContentHash pin on runs.
