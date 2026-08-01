# Admin.Logistics — الإدارة واللوجستيات

| | |
|---|---|
| **قاعدة البيانات** | `admin_db` |
| **المنفذ** | 5090 |
| **DbContext** | [`AdminLogisticsDbContext.cs`](../../src/Services/AdminLogistics/Admin.Logistics.Infrastructure/Data/AdminLogisticsDbContext.cs) |
| **الكيانات** | [`Admin.Logistics.Domain/Entities/`](../../src/Services/AdminLogistics/Admin.Logistics.Domain/Entities/) |

> **أعمدة موروثة:** `BaseEntity` / `AuditableEntity` — راجع [README](README.md).

## مخطط ERD

```mermaid
erDiagram
    Asset ||--o{ AssetAssignment : "Cascade"
    Asset ||--o{ MaintenanceRecord : "Cascade"
    SupplyOrder ||--o{ ReceiptRecord : "SetNull"

    Asset {
        Guid Id PK
        string TagNumber UK
        string Name
        string Category
        AssetStatus Status
        decimal Value
    }

    AssetAssignment {
        Guid Id PK
        Guid AssetId FK
        Guid EmployeeId Ref
        DateTime AssignedAt
        DateTime ReturnedAt
    }

    SupplyOrder {
        Guid Id PK
        Guid RequestedBy Ref
        Guid OrgUnitId Ref
        SupplyOrderStatus Status
        DateTime OrderedAt
    }

    InventoryItem {
        Guid Id PK
        string Sku
        string Name
        int Quantity
        int ReorderThreshold
    }

    MaintenanceRecord {
        Guid Id PK
        Guid AssetId FK
        MaintenanceType MaintenanceType
        decimal Cost
        DateTime PerformedAt
    }

    Vendor {
        Guid Id PK
        string Name
        string ContactInfo
        string Country
    }

    ReceiptRecord {
        Guid Id PK
        Guid SupplyOrderId FK
        Guid ReceivedBy Ref
        DateTime ReceivedAt
    }
```

## فهارس فريدة

| الجدول | الأعمدة |
|--------|---------|
| Asset | TagNumber |

## مراجع خارجية منطقية

| العمود | الجداول | يشير إلى |
|--------|---------|----------|
| `EmployeeId` | AssetAssignment | Employee في **HR.Core** |
| `OrgUnitId` | SupplyOrder | OrgUnit في **HR.Core** |
| `RequestedBy`, `ReceivedBy` | SupplyOrder, ReceiptRecord | مستخدم/موظف |

`InventoryItem` و `Vendor` كيانات مستقلة بدون FK داخلي.
