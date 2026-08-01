# REL-001 — Feature flags (Gateway:EnabledModules)

| بيئة | القيمة | معنى |
|---|---|---|
| **Lab / Compose** | فارغ أو غير معيّن | كل الوحدات مفعّلة (افتراضي الكتالوج) |
| **Staging (Pilot)** | قائمة كاملة أدناه أو فارغ | نفس سطح الإنتاج التجريبي؛ لا وحدات غير منشورة |
| **Production** | قائمة كاملة أدناه أو فارغ | لا تفعيل وحدات غير موجودة في `staging-ha` |

## قائمة الوحدات المعتمدة (Pilot)

```
hr-core, hr-contracts, hr-health, hr-movements, hr-training,
payroll, idcard, records-archive, admin-logistics, it-infra,
dev-analytics, compliance, auth-directory, file-storage
```

مثال تكوين:

```json
"Gateway": {
  "EnabledModules": [
    "hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training",
    "payroll", "idcard", "records-archive", "admin-logistics", "it-infra",
    "dev-analytics", "compliance", "auth-directory", "file-storage"
  ]
}
```

## قواعد

1. لا تُضاف وحدة إلى الإنتاج قبل ظهورها في staging ونجاح rehearsal.
2. إفراغ المصفوفة = تفعيل الكل — مقبول فقط إذا طابقت قائمة الوحدات المنشورة **وكانت كلها Ready** حسب [STR-003 feature-flag gate](./STR_003_FEATURE_FLAG_GATE.md) / [`str-modules/module-catalog.json`](./str-modules/module-catalog.json).
3. Kill-switch مؤقت: أزل الـslug من `EnabledModules` على staging ثم prod بنفس digest promotion (DEP-001).
4. لا تكشف أعلام وحدة `NotReady` في كتالوج المنتج (STR-003).

**Runtime binding (STR-003 production):** Gateway loads `module-catalog.json` via `ModuleCatalog` / `IModuleEntitlementGuard`; Staging/Production enforce Ready ⊆ exposure and path entitlement. See [ADR-STR-003-production-module-binding.md](./ADR-STR-003-production-module-binding.md).

مرجع الكود: [`GatewayModuleCatalog.cs`](../src/Gateway/HRManagement.Gateway/Services/GatewayModuleCatalog.cs).
