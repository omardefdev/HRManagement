# HR.Core — الملفات الشخصية والسير

| | |
|---|---|
| **قاعدة البيانات** | `hr_core_db` |
| **المنفذ** | 5010 |
| **DbContext** | [`HRCoreDbContext.cs`](../../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs) |
| **الكيانات** | [`HR.Core.Domain/Entities/`](../../src/Services/HRCore/HR.Core.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` → `Id`, `TenantId`, `CreatedAt`, `UpdatedAt`. `AuditableEntity` → `CreatedBy`, `UpdatedBy`.

## مخطط ERD

```mermaid
erDiagram
    Employee ||--o{ EmployeeProfile : "Cascade"
    Employee ||--o{ EmploymentRecord : "Cascade"
    Employee ||--o{ EmployeeDocument : "Cascade"
    Employee ||--o{ Qualification : "Cascade"
    Employee ||--o{ ContactInfo : "Cascade"
    Employee ||--o{ EmploymentStatusHistory : "Cascade"
    Employee ||--o{ PerformanceEvaluation : "Cascade"
    Employee ||--o{ ApprovedCV : "Cascade"
    Employee ||--o{ DailyAttendanceRecord : "Cascade"
    Employee }o--o| OrgUnit : "PrimaryOrgUnit SetNull"
    Employee }o--o| RecruitmentApplication : "HiredEmployee SetNull"
    Employee }o--o| DiwanCorrespondence : "RelatedEmployee SetNull"
    OrgUnit ||--o{ OrgUnit : "Parent Children Restrict"
    OrgUnit }o--o| Employee : "HeadEmployee SetNull"
    EmploymentRecord }o--o| OrgUnit : "SetNull"
    EmploymentStatusHistory }o--o| EmploymentRecord : "SetNull"
    WorkforcePlan }o--o| OrgUnit : "SetNull"
    HrStatisticReport }o--o| OrgUnit : "SetNull"
    ApprovalRequest ||--o{ ApprovalStep : "Cascade"

    Employee {
        Guid Id PK
        string NationalId UK
        string FirstName
        string LastName
        string FullName
        DateTime BirthDate
        Gender Gender
        MaritalStatus MaritalStatus
        EmploymentStatus CurrentStatus
        EmployeeCadre Cadre
        Guid PrimaryOrgUnitId FK
        Guid PrimaryContactId
        DateTime HireDate
    }

    OrgUnit {
        Guid Id PK
        string Code UK
        string NameAr
        string NameEn
        Guid ParentId FK
        Guid HeadEmployeeId FK
    }

    EmployeeProfile {
        Guid Id PK
        Guid EmployeeId FK
        string Nationality
        string PlaceOfBirth
        string ResidentialAddress
    }

    EmploymentRecord {
        Guid Id PK
        Guid EmployeeId FK
        Guid OrgUnitId FK
        string PositionTitle
        string PositionCode
        DateTime StartDate
        DateTime EndDate
        EmploymentType EmploymentType
        string Status
    }

    EmployeeDocument {
        Guid Id PK
        Guid EmployeeId FK
        DocumentType DocType
        string DocTitle
        string StorageUrl
        bool Verified
    }

    Qualification {
        Guid Id PK
        Guid EmployeeId FK
        string Title
        string QualificationCode
        DateTime CompletionDate
    }

    ContactInfo {
        Guid Id PK
        Guid EmployeeId FK
        ContactType Type
        string Value
        bool IsPrimary
    }

    EmploymentStatusHistory {
        Guid Id PK
        Guid EmployeeId FK
        Guid EmploymentRecordId FK
        string OldStatus
        string NewStatus
        DateTime ChangedAt
    }

    PerformanceEvaluation {
        Guid Id PK
        Guid EmployeeId FK
        int Year
        decimal Score
        string Status
    }

    ApprovedCV {
        Guid Id PK
        Guid EmployeeId FK
        string DocumentUrl
        DateTime ApprovedAt
    }

    DailyAttendanceRecord {
        Guid Id PK
        Guid EmployeeId FK
        DateOnly AttendanceDate
        DailyAttendanceStatus Status
    }

    WorkforcePlan {
        Guid Id PK
        Guid OrgUnitId FK
        string Title
        int FiscalYear
        string Status
    }

    HrStatisticReport {
        Guid Id PK
        Guid OrgUnitId FK
        string Title
        DateOnly PeriodStart
        DateOnly PeriodEnd
    }

    FieldInspectionVisit {
        Guid Id PK
        string Title
        DateOnly VisitDate
        Guid LeadInspectorId Ref
        string Status
    }

    DiwanCorrespondence {
        Guid Id PK
        string ReferenceNumber
        string Subject
        CorrespondenceDirection Direction
        Guid RelatedEmployeeId FK
    }

    RecruitmentApplication {
        Guid Id PK
        string ApplicantFullName
        string AppliedPosition
        RecruitmentApplicationStatus Status
        Guid HiredEmployeeId FK
    }

    ApprovalRequest {
        Guid Id PK
        string EntityType
        Guid EntityId
        ApprovalStatus Status
        int CurrentLevel
    }

    ApprovalStep {
        Guid Id PK
        Guid ApprovalRequestId FK
        int Level UK
        string RoleRequired
        ApprovalStatus Status
    }

    IntegrationMessageReceipt {
        Guid Id PK
        string MessageId UK
        string ConsumerType UK
        DateTime ReceivedAt
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| Employee | NationalId |
| OrgUnit | Code |
| IntegrationMessageReceipt | MessageId + ConsumerType |
| ApprovalStep | ApprovalRequestId + Level |
| DailyAttendanceRecord | EmployeeId + AttendanceDate (index) |
| ApprovalRequest | EntityType + EntityId, Status (index) |

## مراجع خارجية منطقية

| العمود | الجدول | يشير إلى |
|--------|--------|----------|
| `LeadInspectorId` | FieldInspectionVisit | موظف (HR.Core) |
| `ChangedBy` | EmploymentStatusHistory | مستخدم/موظف |
| `ApprovedBy` | EmploymentRecord, ApprovedCV | مستخدم/موظف |
| `RequestedBy`, `ReviewedBy` | ApprovalRequest, ApprovalStep | Auth / HR.Core |
| `EvaluatorId` | PerformanceEvaluation | موظف |
