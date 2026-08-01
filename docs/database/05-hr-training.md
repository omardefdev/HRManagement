# HR.Training — التدريب والتأهيل

| | |
|---|---|
| **قاعدة البيانات** | `hr_training_db` |
| **المنفذ** | 5050 |
| **DbContext** | [`TrainingDbContext.cs`](../../src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs) |
| **الكيانات** | [`HR.Training.Domain/Entities/`](../../src/Services/HRTraining/HR.Training.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    TrainingProvider ||--o{ TrainingCourse : "SetNull"
    TrainingCourse ||--o{ TrainingEnrollment : "Restrict"
    TrainingCourse ||--o{ TrainingNomination : "Restrict"
    TrainingEnrollment ||--|| TrainingResult : "Cascade"

    TrainingProvider {
        Guid Id PK
        string Name
        string ContactInfo
        string Country
    }

    TrainingCourse {
        Guid Id PK
        string Code UK
        string TitleAr
        string TitleEn
        int DurationDays
        Guid ProviderId FK
    }

    TrainingEnrollment {
        Guid Id PK
        Guid CourseId FK
        Guid EmployeeId Ref
        DateTime EnrolledAt
        EnrollmentStatus Status
        decimal FinalScore
    }

    TrainingResult {
        Guid Id PK
        Guid EnrollmentId FK
        decimal Score
        Guid GradedBy Ref
    }

    QualificationCode {
        Guid Id PK
        string Code UK
        string TitleAr
        string RequiredCourses
    }

    TrainingNomination {
        Guid Id PK
        Guid EmployeeId Ref
        Guid CourseId FK
        ApprovalStatus Status
        Guid NominatedBy Ref
    }

    MissionFile {
        Guid Id PK
        Guid EmployeeId Ref
        string Destination
        DateOnly StartDate
        MissionStatus Status
    }

    TrainingPlan {
        Guid Id PK
        int Year
        Guid OrgUnitId Ref
        Guid ApprovedBy Ref
    }
```

## فهارس

| الجدول | الأعمدة |
|--------|---------|
| TrainingCourse | Code (UK) |
| QualificationCode | Code (UK) |
| MissionFile | EmployeeId |
| TrainingNomination | EmployeeId + CourseId |
| TrainingPlan | Year + OrgUnitId |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | TrainingEnrollment, TrainingNomination, MissionFile | Employee في **HR.Core** |
| `OrgUnitId` | TrainingPlan | OrgUnit في **HR.Core** |
| `QualificationCode` | HR.Core.Qualification (منطقي) | رموز مؤهلات مشتركة |
