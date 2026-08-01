# HR.Contracts — العقود والخدمة

| | |
|---|---|
| **قاعدة البيانات** | `hr_contracts_db` |
| **المنفذ** | 5020 |
| **DbContext** | [`ContractsDbContext.cs`](../../src/Services/HRContracts/HR.Contracts.Infrastructure/Data/ContractsDbContext.cs) |
| **الكيانات** | [`HR.Contracts.Domain/Entities/`](../../src/Services/HRContracts/HR.Contracts.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    ContractType ||--o{ Contract : "Restrict"
    Contract ||--o{ ContractSignature : "Cascade"
    Contract ||--o{ ProbationRecord : "Restrict"
    Contract ||--o{ SeparationRecord : "Restrict"
    Contract ||--o{ CommencementRecord : "Restrict"
    ServiceTransaction ||--o{ ServiceTransactionStateHistory : "Cascade"

    ContractType {
        Guid Id PK
        string Code UK
        string NameAr
        string NameEn
    }

    Contract {
        Guid Id PK
        Guid EmployeeId Ref
        Guid ContractTypeId FK
        DateOnly StartDate
        DateOnly EndDate
        ContractStatus Status
        Guid SignedBy Ref
    }

    ContractSignature {
        Guid Id PK
        Guid ContractId FK
        Guid SignedBy Ref
        DateTime SignedAt
        SignatureType SignatureType
    }

    ProbationRecord {
        Guid Id PK
        Guid ContractId FK
        Guid EmployeeId Ref
        DateOnly StartDate
        DateOnly EndDate
        ProbationResult Result
    }

    SeparationRecord {
        Guid Id PK
        Guid EmployeeId Ref
        Guid ContractId FK
        SeparationType SeparationType
        DateOnly EffectiveDate
    }

    LeaveRequest {
        Guid Id PK
        Guid EmployeeId Ref
        LeaveType LeaveType
        DateOnly StartDate
        DateOnly EndDate
        int DurationDays
        ApprovalStatus Status
    }

    AdvanceRequest {
        Guid Id PK
        Guid EmployeeId Ref
        decimal Amount
        ApprovalStatus Status
    }

    AdministrativeDecision {
        Guid Id PK
        string Subject
        string DecisionNumber
        Guid RelatedEmployeeId Ref
    }

    CommencementRecord {
        Guid Id PK
        Guid EmployeeId Ref
        Guid ContractId FK
        DateOnly StartDate
    }

    ServiceTransaction {
        Guid Id PK
        Guid EmployeeId Ref
        ServiceTransactionType TransactionType
        ServiceTransactionState State
        string ReferenceNumber
    }

    ServiceTransactionStateHistory {
        Guid Id PK
        Guid TransactionId FK
        ServiceTransactionState FromState
        ServiceTransactionState ToState
        Guid ChangedBy Ref
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| ContractType | Code |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | Contract, ProbationRecord, SeparationRecord, LeaveRequest, AdvanceRequest, CommencementRecord, ServiceTransaction | Employee في **HR.Core** |
| `SignedBy`, `ApprovedBy`, `DecisionBy`, `ChangedBy` | متعدد | موظف/مستخدم |
| `RelatedEmployeeId` | AdministrativeDecision | Employee في **HR.Core** |
