# تصنيف كيانات المستأجر (TEN-002)

مصدر الحقيقة لتطبيق عزل المستأجرين عبر ثلاث طبقات:

1. **JWT / TenantIdFlow** — `ITenantContext` + middleware + MassTransit filter
2. **EF Global Query Filters** — `TenantAwareDbContext` + `TenantQueryFilterExtensions`
3. **PostgreSQL RLS** — `TenantRlsMigrationHelper` + `NpgsqlTenantSessionInterceptor` (`app.current_tenant`)

## الفئات

| الفئة | التعريف | EF filter | RLS | أمثلة |
|---|---|---|---|---|
| **global** | بنية منصة / عبر جهات | لا | لا | `Tenant`, `TenantAssignmentRule`, `TenantQuarantineEntry`, `SodRule`, `ConflictingRolePair`, `LegislationSource`, `LegislationKeyArticle`, `LegislationInstrumentLink`, `LegislationSourceAlias`, `EntityAuditLog` |
| **tenant-owned** | صف يخص جهة واحدة (`BaseEntity` غير global) | نعم | نعم | `Employee`, `Contract`, `PayrollRun`, `ExportJob`, … |
| **tenant-scoped (non-BaseEntity)** | يحمل `TenantId` دون وراثة `BaseEntity` | نعم (صريح) | نعم | `IdempotencyRecord`, `OrgUnitSnapshot` |
| **infra (MassTransit)** | جداول البنية التحتية للرسائل | لا | لا | `InboxState`, `OutboxState`, `OutboxMessage` |

## قرارات التصميم

### EntityAuditLog — global عمداً

يحمل `TenantId` لكن يبقى بلا EF filter وبلا RLS حتى يتمكن `platform-admin` من المراجعة عبر الجهات. مسارات استعلام المستأجر **يجب** أن تضيف `.Where(e => e.TenantId == …)`.

وصفه بأنه `global` يخص آلية الاستعلام فقط؛ يجب أن يحتفظ كل سجل بقيمة `TenantId` للكيان الأصلي، ولا تعد `Guid.Empty` قيمة جهة صالحة أو قيمة بديلة للمنصة.

### IntegrationMessageReceipt — tenant-owned

يرث `BaseEntity` وليس في قائمة global → يحصل على EF filter. RLS يُفعَّل عبر `TEN002B_ExtendRls`.

### IdempotencyRecord — tenant-scoped صريح

ليس `BaseEntity`. يُطبَّق عليه filter في `ConfigureTenantQueryFilters` + فهرس فريد `(TenantId, Key)`.

### Saga states

جداول MassTransit saga تحمل `TenantId` حيث يُعرَّف؛ RLS يغطيها إن وُجد العمود. لا EF filter عام (مستودع MassTransit).

### Gateway audit DB

`GatewayAuditDbContext` خارج نموذج الـ 14 خدمة. `SecurityAuditEvent.TenantId` اختياري (أحداث منصة).

### SYR-001 legislation register — global

`LegislationSources` وجداولها الفرعية في `hr_core_db` بلا `TenantId`: تشريع وطني واحد للمنصة. `RecruitmentRequisition` و`OfferLetter` (tenant-owned) يحملان `LegalSourceId` كـ FK nullable إلى `LegislationSources`.

## تطبيق RLS للجداول الجديدة (TEN-002B)

Migration `TEN002B_ExtendRls` في كل خدمة تستدعي:

```csharp
TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables(migrationBuilder);
```

هذا يفعّل RLS على **كل** جدول عام فيه عمود `TenantId` (ما عدا الاستثناءات أعلاه). أي جدول مستقبلي بـ `TenantId` يُغطى عند إعادة تشغيل الـ migration أو إضافة migration مماثلة.

## حارس CI

- `Ten002ArchitecturalTests` — يمنع `IgnoreQueryFilters` خارج `TenantFilterBypass`
- `Ten002ArchitecturalTests` — يتحقق من وجود `TEN002B_ExtendRls` لكل خدمة
- `CrossTenantRlsIntegrationTests` — RLS فعلي على PostgreSQL
- `CrossTenantApiPenetrationTests` — لا تسرب عبر API

## مرجع الكود

| الملف | الدور |
|---|---|
| `TenantEntityClassification.cs` | تصنيف global vs tenant-owned |
| `TenantQueryFilterExtensions.cs` | EF filters |
| `TenantIdStampInterceptor.cs` | ختم/رفض insert |
| `NpgsqlTenantSessionInterceptor.cs` | `app.current_tenant` |
| `TenantRlsMigrationHelper.cs` | SQL RLS |
| `TenantFilterBypass.cs` | تجاوز مدقق (platform-admin فقط) |
