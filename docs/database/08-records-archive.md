# Records.Archive — الأرشيف والملفات

| | |
|---|---|
| **قاعدة البيانات** | `archive_db` |
| **المنفذ** | 5080 |
| **DbContext** | [`ArchiveDbContext.cs`](../../src/Services/RecordsArchive/Records.Archive.Infrastructure/Data/ArchiveDbContext.cs) |
| **الكيانات** | [`Records.Archive.Domain/Entities/`](../../src/Services/RecordsArchive/Records.Archive.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    DocumentClassification ||--o{ ArchiveItem : "SetNull"
    ArchiveItem ||--o{ PhysicalFileRecord : "Cascade"
    ArchiveItem ||--o{ DigitalFileRecord : "Cascade"
    ArchiveItem ||--o{ LoanRecord : "Restrict"
    ArchiveItem ||--o{ ArchiveIndexEntry : "Cascade"
    ArchiveItem ||--o{ DisposalRecord : "Restrict"

    DocumentClassification {
        Guid Id PK
        string Code UK
        string NameAr
        int RetentionYears
    }

    ArchiveItem {
        Guid Id PK
        string ReferenceNumber UK
        string Title
        Guid ClassificationId FK
        ArchiveFormat Format
        Guid Creator Ref
    }

    PhysicalFileRecord {
        Guid Id PK
        Guid ArchiveItemId FK
        string BoxNumber
        int PageCount
    }

    DigitalFileRecord {
        Guid Id PK
        Guid ArchiveItemId FK
        string FileUrl
        long FileSizeBytes
        string Checksum
    }

    LoanRecord {
        Guid Id PK
        Guid ArchiveItemId FK
        string Borrower
        DateTime LoanDate
        LoanStatus Status
    }

    ArchiveIndexEntry {
        Guid Id PK
        Guid ArchiveItemId FK
        string Keywords
        string Summary
    }

    DisposalRecord {
        Guid Id PK
        Guid ArchiveItemId FK
        DateTime DisposalDate
        Guid ApprovedBy Ref
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| ArchiveItem | ReferenceNumber |
| DocumentClassification | Code |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `Creator` | ArchiveItem | موظف/مستخدم |
| `ApprovedBy` | DisposalRecord | موظف/مستخدم |

لا يوجد `EmployeeId` مباشر — الأرشيف يعتمد `ReferenceNumber` وربط الملفات بالموظف عبر البيانات الوصفية أو التكامل.
