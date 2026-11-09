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
| **national shared** (PLAN-48) | جدول tenant-owned تقرأ كلُّ جهة صفوفَ المستأجر الوطني منه (`PLATFORM`)، ولا يكتبها إلا المستأجر الوطني. الكيان يحمل `INationalShared`، والقائمة واحدة: `NationalTenant.TableNames` | نعم، والقراءة الوطنية عبر `OwnOrNational` و`NationalOnly` فقط | `tenant_isolation` للكتابة، وسياسة `national_shared_read` (PERMISSIVE FOR SELECT) للقراءة | `PolicyTemplates`, `PolicyTemplateVersions`, `PolicyRules`, `PolicyFactsSchemas`, `PolicyWorkflowBindings`, `WorkflowDefinitions`, `CodePolicyTemplates`, `CodePolicyVersions`, `CodePolicyRules`, `ReferenceCatalogEntries` |

**القاعدة الوطنية المشتركة (PLAN-48، D-TEN-2):**
- لا يدخل القائمةَ جدولٌ فيه بيانات شخصية. واختبار `NationalSharedArchitectureTests` يُثبت أن القوائم الثلاث واحدة (القائمة، والكيانات المعلّمة في نموذج EF، وجداول السكربت)، وأن لا قراءة لجدول وطني عبر تجاوز عام.
- الكتابة: `NationalSharedWriteGuard` يرفض إضافة صف وطني أو تعديله أو حذفه من جلسة غير وطنية خارج نطاق بذر صريح (`TenantStampScope`)، بـ 403 (`national-shared-write`). وتحت RLS المفروض ترفض القاعدة الكتابة نفسها.
- في الجداول ذات النطاق: «وطني» في طبقة التطبيق هو `Scope = National` فقط (D-TEN-1)؛ فتجاوزات الجهة التجريبية داخل `PLATFORM` لا تُقرأ في الجهات.

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
