# Payroll — الرواتب والتعويضات

| | |
|---|---|
| **قاعدة البيانات** | `payroll_db` |
| **المنفذ** | 5060 |
| **DbContext** | [`PayrollDbContext.cs`](../../src/Services/Payroll/Payroll.Infrastructure/Data/PayrollDbContext.cs) |
| **الكيانات** | [`Payroll.Domain/Entities/`](../../src/Services/Payroll/Payroll.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md). جميع `decimal` بدقة 18,2.

## مخطط ERD

```mermaid
erDiagram
    PayrollRun ||--o{ SalaryLine : "Restrict"
    PayrollRun ||--o{ PayrollAuditRecord : "Restrict"
    PayrollRun ||--o{ PaymentBatch : "Restrict"
    PayrollRun ||--o{ PayrollAdjustment : "SetNull"
    PayrollRun ||--o{ DeductionRecord : "SetNull"
    CompensationType ||--o{ AllowanceRecord : "Restrict"

    PayrollRun {
        Guid Id PK
        DateTime PeriodStart
        DateTime PeriodEnd
        PayrollRunStatus Status
        decimal TotalNetPay
    }

    SalaryLine {
        Guid Id PK
        Guid PayrollRunId FK
        Guid EmployeeId Ref
        decimal GrossPay
        decimal TotalDeductions
        decimal NetPay
    }

    CompensationType {
        Guid Id PK
        string Code UK
        string NameAr
        string CalculationFormula
    }

    AllowanceRecord {
        Guid Id PK
        Guid EmployeeId Ref
        Guid AllowanceTypeId FK
        decimal Amount
        DateTime EffectiveFrom
    }

    DeductionRecord {
        Guid Id PK
        Guid EmployeeId Ref
        string DeductionType
        decimal Amount
        Guid AppliedInRunId FK
    }

    PayrollAdjustment {
        Guid Id PK
        Guid EmployeeId Ref
        Guid PayrollRunId FK
        decimal Amount
        string AdjustmentReason
    }

    PayrollAuditRecord {
        Guid Id PK
        Guid PayrollRunId FK
        Guid AuditorId Ref
        AuditStatus Status
    }

    PaymentBatch {
        Guid Id PK
        Guid PayrollRunId FK
        decimal TotalAmount
        string BankTransferReference
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| CompensationType | Code |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | SalaryLine, AllowanceRecord, DeductionRecord, PayrollAdjustment | Employee في **HR.Core** |
| `AuditorId` | PayrollAuditRecord | مستخدم/موظف |
