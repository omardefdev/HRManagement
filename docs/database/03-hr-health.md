# HR.Health — الصحة واللجان الطبية

| | |
|---|---|
| **قاعدة البيانات** | `hr_health_db` |
| **المنفذ** | 5030 |
| **DbContext** | [`HealthDbContext.cs`](../../src/Services/HRHealth/HR.Health.Infrastructure/Data/HealthDbContext.cs) |
| **الكيانات** | [`HR.Health.Domain/Entities/`](../../src/Services/HRHealth/HR.Health.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    MedicalRecord ||--o{ MedicalCommitteeReport : "Cascade"
    MedicalCommitteeReport ||--o{ MedicalRecommendation : "SetNull"

    MedicalRecord {
        Guid Id PK
        Guid EmployeeId Ref
        string MedicalHistory
        string Allergies
        DateOnly LastExamDate
    }

    MedicalCommitteeReport {
        Guid Id PK
        Guid MedicalRecordId FK
        DateOnly CommitteeDate
        string Findings
        MedicalCommitteeRecommendation Recommendation
    }

    MedicalRecommendation {
        Guid Id PK
        Guid ReportId FK
        Guid EmployeeId Ref
        MedicalRecommendationType RecommendationType
        string Details
    }

    PeriodicMedicalCheck {
        Guid Id PK
        Guid EmployeeId Ref
        DateOnly CheckDate
        DateOnly NextDue
    }

    InsuranceCard {
        Guid Id PK
        Guid EmployeeId Ref
        string MemberNumber
        DateOnly IssueDate
        DateOnly ExpiryDate
    }

    HealthStatusEntry {
        Guid Id PK
        Guid EmployeeId Ref
        DateTime EntryDate
        string StatusCode
        Guid CreatedBy Ref
    }

    HealthRetirementRecord {
        Guid Id PK
        Guid EmployeeId Ref
        DateOnly EffectiveDate
        string Reason
    }

    InsuranceContract {
        Guid Id PK
        string Provider
        DateOnly StartDate
        DateOnly EndDate
    }
```

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | MedicalRecord, PeriodicMedicalCheck, InsuranceCard, HealthStatusEntry, MedicalRecommendation, HealthRetirementRecord | Employee في **HR.Core** |
| `CreatedBy` | HealthStatusEntry | مستخدم/موظف |

`InsuranceContract` لا يرتبط بموظف على مستوى DB — عقد مزود تأمين عام.
