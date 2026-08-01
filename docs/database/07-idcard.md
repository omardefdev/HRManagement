# IDCard — الهويات والبطاقات

| | |
|---|---|
| **قاعدة البيانات** | `idcard_db` |
| **المنفذ** | 5070 |
| **DbContext** | [`IDCardDbContext.cs`](../../src/Services/IDCard/IDCard.Infrastructure/Data/IDCardDbContext.cs) |
| **الكيانات** | [`IDCard.Domain/Entities/`](../../src/Services/IDCard/IDCard.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    CardDesign ||--o{ IssuedCard : "Restrict"
    CardDeliveryRecord ||--o| IssuedCard : "DeliveryRecord SetNull"
    IssuedCard ||--o{ AccessPermission : "Cascade"

    CardDesign {
        Guid Id PK
        string Name
        string Template
        int ValidityDays
    }

    IssuedCard {
        Guid Id PK
        Guid EmployeeId Ref
        Guid CardDesignId FK
        string SerialNumber
        CardStatus Status
        DateTime IssuedAt
        DateOnly ExpiresAt
        Guid DeliveryRecordId FK
    }

    CardRequest {
        Guid Id PK
        Guid EmployeeId Ref
        Guid RequestedBy Ref
        CardRequestPurpose Purpose
        CardRequestStatus Status
    }

    CardDeliveryRecord {
        Guid Id PK
        Guid IssuedCardId
        Guid PrintedBy Ref
        Guid DeliveredTo Ref
        DateTime DeliveredAt
    }

    AccessPermission {
        Guid Id PK
        Guid IssuedCardId FK
        string Resource
        string AccessLevel
    }

    OfficialSealRecord {
        Guid Id PK
        string Name
        string SealImageUrl
        DateOnly ValidFrom
    }

    CardPermissionSet {
        Guid Id PK
        string Name
        string Permissions
    }
```

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | IssuedCard, CardRequest | Employee في **HR.Core** |
| `RequestedBy`, `PrintedBy`, `DeliveredTo` | CardRequest, CardDeliveryRecord | مستخدم/موظف |

`CardPermissionSet` و `OfficialSealRecord` كيانات مستقلة (قوالب صلاحيات وأختام).
