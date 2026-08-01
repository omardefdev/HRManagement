# HR.Movements — التنقلات والتوزيع

| | |
|---|---|
| **قاعدة البيانات** | `hr_movements_db` |
| **المنفذ** | 5040 |
| **DbContext** | [`MovementsDbContext.cs`](../../src/Services/HRMovements/HR.Movements.Infrastructure/Data/MovementsDbContext.cs) |
| **الكيانات** | [`HR.Movements.Domain/Entities/`](../../src/Services/HRMovements/HR.Movements.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    TransferRequest ||--o{ MovementExecutionRecord : "SetNull"
    TransferRequest ||--o{ TransferApproval : "Cascade"
    TransferRequest ||--o{ TransferFinancialImpact : "Cascade"

    TransferRequest {
        Guid Id PK
        Guid EmployeeId Ref
        Guid FromOrgUnitId Ref
        Guid ToOrgUnitId Ref
        DateOnly RequestDate
        ApprovalStatus Status
        Guid ApprovedBy Ref
    }

    MovementNotice {
        Guid Id PK
        string Title
        DateOnly IssueDate
        Guid IssuedBy Ref
    }

    DistributionPlan {
        Guid Id PK
        Guid OrgUnitId Ref
        DateOnly EffectiveDate
        Guid CreatedBy Ref
    }

    MovementExecutionRecord {
        Guid Id PK
        Guid TransferRequestId FK
        Guid EmployeeId Ref
        DateTime ExecutedAt
        Guid ExecutedBy Ref
    }

    SecondmentRequest {
        Guid Id PK
        Guid EmployeeId Ref
        string HostOrg
        DateOnly StartDate
        DateOnly EndDate
        SecondmentStatus Status
    }

    TransferApproval {
        Guid Id PK
        Guid TransferRequestId FK
        Guid ApprovedBy Ref
        DateTime ApprovedAt
    }

    TransferFinancialImpact {
        Guid Id PK
        Guid TransferRequestId FK
        Guid EmployeeId Ref
        decimal SalaryChange
        decimal AllowanceChange
    }
```

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | TransferRequest, MovementExecutionRecord, SecondmentRequest, TransferFinancialImpact | Employee في **HR.Core** |
| `FromOrgUnitId`, `ToOrgUnitId` | TransferRequest | OrgUnit في **HR.Core** |
| `OrgUnitId` | DistributionPlan | OrgUnit في **HR.Core** |
| `RequestedBy`, `ApprovedBy`, `ExecutedBy`, `IssuedBy`, `CreatedBy` | متعدد | مستخدم/موظف |

التأثير المالي (`TransferFinancialImpact`) يرتبط منطقياً بخدمة **Payroll** عبر الأحداث.
