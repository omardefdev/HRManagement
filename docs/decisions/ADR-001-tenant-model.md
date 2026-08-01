# ADR-001 — نموذج الجهات (Tenant Model)

| الحقل | القيمة |
|-------|--------|
| الحالة | Accepted |
| البطاقة | TEN-001 / TEN-002 |
| التاريخ | 2026-07-28 |

## السياق

المنصة الوطنية للموارد البشرية تخدم وزارات وهيئات متعددة. TEN-001 عرّف الجهة وسياقات الحمل. TEN-002 فرض العزل على طبقة البيانات.

## القرار

1. **Tenant = وزارة/هيئة حكومية** (Agency)، وليس فرعاً ولا وحدة تنظيمية.
2. **الهيكل داخل الجهة:** `Tenant → OrgUnit tree → Branches`. نطاق الفرع (IAM-002) يعمل **داخل** حدود الجهة فقط.
3. **قاعدة بيانات مشتركة** مع عزل منطقي عبر `TenantId` + مرشحات EF + PostgreSQL RLS.
4. **Realm Keycloak واحد**؛ هوية الجهة عبر claim/attribute موثوق: `tenant_id`.
5. **العميل لا يختار الجهة** — تُستخرج من JWT فقط (أو من سياق الرسالة/الوظيفة الخلفية).
6. **مشغّل المنصة** (`platform-admin`) يمكنه العمل عبر الجهات عبر `TenantFilterBypass` فقط.
7. **نقل بين الجهات** يتطلب مساراً صريحاً (Saga لاحق في INT-003/FUN-003).

## تنفيذ TEN-002

- `BaseEntity.TenantId` أصبح `Guid` غير قابل للعدم.
- `TenantAwareDbContext` + `ConfigureTenantQueryFilters` على 14 خدمة.
- `TenantIdStampInterceptor` يرفض الإدراج المملوك بلا tenant.
- `NpgsqlTenantSessionInterceptor` يضبط `app.current_tenant` لـ RLS.
- ترحيلات per-service: backfill للجهة الافتراضية `TenantDefaults.DefaultTenantId`، NOT NULL، فهارس مركّبة، سياسات RLS.
- فهرس فريد مركّب: `(TenantId, NationalId)` للموظفين، `(TenantId, Code)` للوحدات، `(TenantId, Username/Email)` للمستخدمين.

## البدائل المرفوضة

| البديل | السبب |
|--------|-------|
| Realm لكل جهة | تعقيد هوية وتشغيل غير مبرر |
| قاعدة بيانات لكل جهة | تكلفة تشغيل وترحيل عالية |
| Tenant = OrgUnit جذر | يخلط الملكية القانونية مع الهيكل الإداري |

## العواقب

- TEN-003 يرحّل البيانات من الجهة الافتراضية إلى جهات حقيقية ويغلق اختبارات العبور.
