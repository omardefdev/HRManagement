# Auth.Directory — المصادقة والدليل

| | |
|---|---|
| **قاعدة البيانات** | `auth_directory_db` |
| **المنفذ** | 5130 |
| **DbContext** | [`AuthDirectoryDbContext.cs`](../../src/Services/AuthDirectory/Auth.Directory.Infrastructure/Data/AuthDirectoryDbContext.cs) |
| **الكيانات** | [`Auth.Directory.Domain/Entities/`](../../src/Services/AuthDirectory/Auth.Directory.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    AuthUser ||--o{ AuthToken : "Cascade"
    AuthUser ||--o{ UserRole : "Cascade"
    Role ||--o{ UserRole : "Cascade"

    AuthUser {
        Guid Id PK
        string Username UK
        string Email UK
        string PasswordHash
        Guid EmployeeId Ref
        string KeycloakUserId
        bool IsActive
        DateTime LastLoginAt
    }

    Role {
        Guid Id PK
        string Name
        string Description
        string Permissions
    }

    UserRole {
        Guid Id PK
        Guid AuthUserId FK
        Guid RoleId FK
        DateTime AssignedAt
        string AssignedBy
    }

    AuthToken {
        Guid Id PK
        Guid UserId FK
        string Token
        DateTime ExpiresAt
        bool Revoked
    }

    DirectoryEntry {
        Guid Id PK
        Guid EmployeeId Ref
        string DisplayName
        string PrimaryEmail
        Guid PrimaryOrgUnitId Ref
        DateTime LastSyncedAt
    }

    DirectorySyncRecord {
        Guid Id PK
        string SourceService
        int RecordsProcessed
        DateTime StartedAt
        DateTime CompletedAt
    }

    PermissionPolicy {
        Guid Id PK
        string Name
        string Rules
    }

    AuthAuditLog {
        Guid Id PK
        string Action
        string EntityType
        Guid EntityId
        string PerformedBy
        DateTime PerformedAt
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| AuthUser | Username, Email |
| UserRole | AuthUserId + RoleId |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | AuthUser, DirectoryEntry | Employee في **HR.Core** |
| `PrimaryOrgUnitId` | DirectoryEntry | OrgUnit في **HR.Core** |
| `KeycloakUserId` | AuthUser | مستخدم Keycloak (خارج DB) |

`PermissionPolicy` و `DirectorySyncRecord` و `AuthAuditLog` كيانات دعم بدون FK لموظف.
