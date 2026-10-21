# FUN-005 — مصفوفة رحلات الهوية والأصول وتقنية المعلومات

المصدر: [`fun-identity/journey-matrix.json`](fun-identity/journey-matrix.json) — انظر [ADR-FUN-005](ADR-FUN-005-identity-assets-it-closure.md).

## IDCard

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| Requested | نعم | idcard | 24 | `CardRequestsController` |
| Designed | لا | idcard | 48 | `CardDesignsController` |
| Issued | نعم | idcard | 48 | `IssuedCards` + `ICardIssued` |
| Printed | نعم | idcard | 24 | `MarkPrinted` |
| Delivered | نعم | idcard | 24 | `MarkDelivered` + دليل |
| Revoked | نعم | idcard | 48 | party ACK + `UrgentRevoke` |

## Assets

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| Assigned | نعم | admin-logistics | 24 | `AssignToEmployee` (حائز واحد) |
| Returned | نعم | admin-logistics | 72 | `ReturnAssignment` |
| Lost | لا | admin-logistics | 24 | `MarkLost` → workflow للمميز؛ `AssetLossReport` |
| Recover | لا | admin-logistics | 24 | `RecoverAsset` → InStorage |
| SeparationReturn | نعم | admin-logistics | 48 | `SeparationCompletedConsumer` |

**تصنيف «مميز»:** `Asset.IsPrivileged` = نتيجة إجراء سياسة `Classify` (SetField) عند الإنشاء/التعديل — ليس checkbox يدوياً. المعايير (قيمة، فئة، …) في محرك السياسات (`R-CLASSIFY-*`).

## ITProvision

| خطوة | إلزامي | مالك | SLA | واجهة |
|---|---|---|---|---|
| HireTicket | نعم | it-infra | 24 | `EmployeeCreatedConsumer` |
| DirectorySync | نعم | it-infra | 48 | `ProvisioningStatus` |
| RetryOnFail | لا | it-infra | 24 | `RetryProvision` |
| SeparationDetach | نعم | it-infra | 48 | فصل أجهزة + صلاحيات |

لا saga Automatonymous جديد — INT-003 parties + أحداث قائمة.
