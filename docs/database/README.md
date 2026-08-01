# مخططات قواعد البيانات — نظام إدارة الموارد البشرية

توثيق ERD لجميع قواعد بيانات PostgreSQL في النظام (13 خدمة مصغرة، 111 كياناً). المخططات بصيغة **Mermaid** وتُعرض مباشرة في Cursor و GitHub.

## اصطلاحات القراءة

| الرمز | المعنى |
|-------|--------|
| **PK** | المفتاح الأساسي (`Id: Guid`) |
| **FK** | مفتاح أجنبي داخل نفس قاعدة البيانات |
| **UK** | فهرس فريد |
| **Ref** | مرجع منطقي لخدمة أخرى (بدون قيد FK على مستوى DB) |

### الأعمدة الموروثة (لا تُكرَّر في كل جدول)

- **`BaseEntity`:** `Id` (PK), `TenantId`, `CreatedAt`, `UpdatedAt`
- **`AuditableEntity`** (إضافةً على ما سبق): `CreatedBy`, `UpdatedBy`

المصدر: [`BaseEntity.cs`](../../src/Shared/HRManagement.SharedKernel/Entities/BaseEntity.cs)، [`AuditableEntity.cs`](../../src/Shared/HRManagement.SharedKernel/Entities/AuditableEntity.cs)

### سلوك الحذف في العلاقات

يُذكر بجانب كل علاقة في المخطط: `Cascade`، `Restrict`، أو `SetNull` — كما في `OnModelCreating` لكل `DbContext`.

---

## الفهرس

| # | الملف | الخدمة | قاعدة البيانات | المنفذ | الكيانات |
|---|-------|--------|----------------|--------|----------|
| — | [00-overview.md](00-overview.md) | نظرة عامة + علاقات بين الخدمات | — | — | — |
| 1 | [01-hr-core.md](01-hr-core.md) | HR.Core | `hr_core_db` | 5010 | 19 |
| 2 | [02-hr-contracts.md](02-hr-contracts.md) | HR.Contracts | `hr_contracts_db` | 5020 | 11 |
| 3 | [03-hr-health.md](03-hr-health.md) | HR.Health | `hr_health_db` | 5030 | 8 |
| 4 | [04-hr-movements.md](04-hr-movements.md) | HR.Movements | `hr_movements_db` | 5040 | 7 |
| 5 | [05-hr-training.md](05-hr-training.md) | HR.Training | `hr_training_db` | 5050 | 8 |
| 6 | [06-payroll.md](06-payroll.md) | Payroll | `payroll_db` | 5060 | 8 |
| 7 | [07-idcard.md](07-idcard.md) | IDCard | `idcard_db` | 5070 | 7 |
| 8 | [08-records-archive.md](08-records-archive.md) | Records.Archive | `archive_db` | 5080 | 7 |
| 9 | [09-admin-logistics.md](09-admin-logistics.md) | Admin.Logistics | `admin_db` | 5090 | 7 |
| 10 | [10-it-infrastructure.md](10-it-infrastructure.md) | IT.Infra | `it_infra_db` | 5100 | 7 |
| 11 | [11-dev-analytics.md](11-dev-analytics.md) | Dev.Analytics | `analytics_db` | 5110 | 7 |
| 12 | [12-compliance.md](12-compliance.md) | Compliance | `compliance_db` | 5120 | 7 |
| 13 | [13-auth-directory.md](13-auth-directory.md) | Auth.Directory | `auth_directory_db` | 5130 | 8 |

---

## المصدر

المخططات مُستخرجة من:

- `src/Services/{Service}/{Service}.Domain/Entities/`
- `src/Services/{Service}/{Service}.Infrastructure/Data/*DbContext.cs`

لا تُشغَّل ترحيلات EF لإنشاء هذا التوثيق — الشيفرة المصدرية هي المرجع.
