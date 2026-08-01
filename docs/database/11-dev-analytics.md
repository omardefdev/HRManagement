# Dev.Analytics — التطوير والتحليل

| | |
|---|---|
| **قاعدة البيانات** | `analytics_db` |
| **المنفذ** | 5110 |
| **DbContext** | [`AnalyticsDbContext.cs`](../../src/Services/DevAnalytics/Dev.Analytics.Infrastructure/Data/AnalyticsDbContext.cs) |
| **الكيانات** | [`Dev.Analytics.Domain/Entities/`](../../src/Services/DevAnalytics/Dev.Analytics.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    ReadModel ||--o{ DataRefreshSchedule : "SetNull"

    Report {
        Guid Id PK
        string Title
        ReportType ReportType
        Guid GeneratedBy Ref
        string Url
    }

    Dashboard {
        Guid Id PK
        string Name
        string Widgets
        Guid OwnerId Ref
    }

    ETLJob {
        Guid Id PK
        string Name
        string Schedule
        JobStatus Status
        DateTime LastRun
    }

    ReadModel {
        Guid Id PK
        string Name
        string Schema
        DateTime LastRefreshed
    }

    KPI {
        Guid Id PK
        string Code UK
        string Name
        decimal TargetValue
        decimal CurrentValue
    }

    AnalyticsEvent {
        Guid Id PK
        string EventName
        string Payload
        DateTime OccurredAt
    }

    DataRefreshSchedule {
        Guid Id PK
        Guid TargetReadModelId FK
        string CronExpression
        DateTime NextRunAt
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| KPI | Code |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `GeneratedBy`, `OwnerId` | Report, Dashboard | مستخدم (**Auth.Directory**) |

نماذج القراءة (`ReadModel`) تُحدَّث عبر ETL من بيانات الخدمات الأخرى (أحداث RabbitMQ) — بدون FK.
