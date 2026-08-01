# Compliance — الامتثال والرقابة

| | |
|---|---|
| **قاعدة البيانات** | `compliance_db` |
| **المنفذ** | 5120 |
| **DbContext** | [`ComplianceDbContext.cs`](../../src/Services/Compliance/Compliance.Infrastructure/Data/ComplianceDbContext.cs) |
| **الكيانات** | [`Compliance.Domain/Entities/`](../../src/Services/Compliance/Compliance.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    Complaint ||--o{ ComplaintFollowUp : "Cascade"
    AuditFinding ||--o{ ComplianceRecommendation : "Cascade"

    Complaint {
        Guid Id PK
        string FiledBy
        string Subject
        ComplaintStatus Status
        Guid AssignedTo Ref
        DateTime FiledAt
    }

    ComplaintFollowUp {
        Guid Id PK
        Guid ComplaintId FK
        string ActionTaken
        Guid PerformedBy Ref
        DateTime PerformedAt
    }

    AuditFinding {
        Guid Id PK
        Guid AuditId
        FindingSeverity Severity
        FindingStatus Status
        string Description
    }

    ComplianceRecommendation {
        Guid Id PK
        Guid FindingId FK
        string RecommendedAction
        Guid ResponsibleUnitId Ref
        ApprovalStatus Status
    }

    LegalCase {
        Guid Id PK
        string CaseNumber
        string Subject
        CaseStatus Status
    }

    DisciplinaryAction {
        Guid Id PK
        Guid EmployeeId Ref
        ActionType ActionType
        Guid IssuedBy Ref
        AppealStatus AppealStatus
    }

    LegalReferral {
        Guid Id PK
        Guid OriginatingUnit Ref
        string ReferredTo
        DateTime ReferralDate
    }
```

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | DisciplinaryAction | Employee في **HR.Core** |
| `ResponsibleUnitId` | ComplianceRecommendation | OrgUnit في **HR.Core** |
| `OriginatingUnit` | LegalReferral | OrgUnit في **HR.Core** |
| `AssignedTo`, `PerformedBy`, `IssuedBy` | متعدد | مستخدم/موظف |

`LegalCase` مستقل عن الموظف على مستوى DB.
