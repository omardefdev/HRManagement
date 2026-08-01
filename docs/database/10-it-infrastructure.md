# IT.Infra — تقنية المعلومات

| | |
|---|---|
| **قاعدة البيانات** | `it_infra_db` |
| **المنفذ** | 5100 |
| **DbContext** | [`ITInfraDbContext.cs`](../../src/Services/ITInfrastructure/IT.Infra.Infrastructure/Data/ITInfraDbContext.cs) |
| **الكيانات** | [`IT.Infrastructure.Domain/Entities/`](../../src/Services/ITInfrastructure/IT.Infrastructure.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

`ITInfraDbContext` **لا يعرّف** `OnModelCreating` — لا علاقات FK صريحة بين الجداول؛ الربط بالموظفين/المستخدمين عبر `Guid` فقط.

## مخطط ERD

```mermaid
erDiagram
    ServerDevice {
        Guid Id PK
        string Hostname
        string IpAddress
        string Os
        ServerStatus Status
        string Location
    }

    UserDevice {
        Guid Id PK
        string AssetTag
        Guid AssignedTo Ref
        DeviceType DeviceType
        DeviceStatus Status
    }

    BackupJob {
        Guid Id PK
        string Target
        string Schedule
        BackupStatus Status
        DateTime LastRunAt
    }

    SecurityIncident {
        Guid Id PK
        string Title
        Severity Severity
        TicketStatus Status
        Guid HandledBy Ref
    }

    SystemPermissionRecord {
        Guid Id PK
        Guid UserId Ref
        string System
        string Roles
        DateTime GrantedAt
    }

    ITSupportTicket {
        Guid Id PK
        Guid ReportedBy Ref
        string IssueSummary
        TicketStatus Status
        Guid AssignedTo Ref
    }

    EnvironmentConfiguration {
        Guid Id PK
        string Name
        string Config
        string Version
    }
```

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `AssignedTo` | UserDevice | Employee (**HR.Core**) أو مستخدم |
| `UserId` | SystemPermissionRecord | AuthUser (**Auth.Directory**) |
| `ReportedBy`, `AssignedTo`, `HandledBy` | ITSupportTicket, SecurityIncident | مستخدم/موظف |
