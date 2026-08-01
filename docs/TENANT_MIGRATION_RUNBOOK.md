# دليل تشغيل ترحيل الجهات (TEN-003)

تشغيل إسناد السجلات من الجهة الافتراضية (`PLATFORM` / `00000000-0000-4000-8000-000000000001`) إلى جهات حقيقية، مع الحجر والمصالحة.

## 1. المتطلبات المسبقة

- [ ] TEN-002 مطبّق بالكامل (TenantId NOT NULL، EF filters، RLS، migrations `TEN002_EnforceTenantId`)
- [ ] ترحيل HRCore يتضمن `TEN003_TenantAssignmentAndQuarantine` (جداول `TenantAssignmentRules` و`TenantQuarantineEntries`)
- [ ] نسخة احتياطية كاملة لكل قواعد البيانات (`scripts/backup-all-databases.ps1`)
- [ ] جهات حقيقية مُدرَجة في `hr_core."Tenants"` (غير PLATFORM)، مثال MOF / MOH
- [ ] وحدات تنظيمية جذرية برمز يطابق `Tenant.Code` حيث يُستخدم اكتشاف OrgUnit
- [ ] صلاحيات `psql` و`PGPASSWORD` متاحة على بيئة التشغيل
- [ ] نافذة صيانة متفق عليها (staging أولاً، ثم production)

## 2. Dry-run على نسخة staging

```powershell
# من جذر المستودع
$env:PGPASSWORD = "<secret>"

# معاينة بدون كتابة (ما عدا قراءة)
.\scripts\assign-tenants.ps1 -DryRun -PostgresHost localhost -ReportPath .\reports\assign-dryrun.json

# مصالحة قبل الإسناد (خط أساس)
.\scripts\reconcile-tenants.ps1 -ReportPath .\reports\reconcile-before.json
```

راجع التقرير:

- عدد الصفوف على الجهة الافتراضية
- قواعد `TenantAssignmentRules` الصريحة إن وُجدت
- تطابق رموز الجذور مع `Tenants.Code`

## 3. تنفيذ الإسناد

```powershell
.\scripts\assign-tenants.ps1 -PostgresHost localhost -ReportPath .\reports\assign-live.json
.\scripts\reconcile-tenants.ps1 -ReportPath .\reports\reconcile-after.json
```

السلوك المتوقع:

1. جذور `OrgUnits` ذات `Code` = `Tenant.Code` تُنسب للجهة
2. الأبناء عبر `MaterializedPath` / `ParentId`
3. الموظفون عبر `PrimaryOrgUnitId`
4. قواعد `Explicit` من جدول القواعد
5. كل صف يبقى على PLATFORM يُدرَج في `TenantQuarantineEntries` (upsert مركب)

الإعادة آمنة (idempotent): التحديثات تقتصر على `WHERE "TenantId" = DEFAULT`.

## 4. التحقق من المصالحة

معايير القبول:

| فحص | معيار النجاح |
|-----|---------------|
| التغطية | كل صف إما منسوب لجهة حقيقية أو في الحجر |
| الأيتام | `OrphanRows` على PLATFORM = 0 أو كلها محجورة |
| FK عبر الجهات | `CrossTenantFkOrphans.MismatchCount = 0` |
| Checksums | إجمالي الصفوف قبل/بعد متطابق (لا حذف) |

اختبارات الاختراق الأفقية (محلياً / CI):

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~CrossTenant"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~CrossTenant"
# RLS الكامل يتطلب:
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~CrossTenantRls"
```

## 5. التراجع (Rollback)

لكل جدول أُسند إلى جهة `@assignedTenantId`:

```sql
UPDATE "{Table}"
SET "TenantId" = '00000000-0000-4000-8000-000000000001'
WHERE "TenantId" = @assignedTenantId;
```

ثم أعد تشغيل المصالحة. سجلات الحجر تبقى للمراجعة؛ يمكن وسمها بـ `Resolution = 'RolledBack'` يدوياً.

استعادة من النسخة الاحتياطية مفضّلة إذا تجاوز الإسناد نطاقاً غير متوقع.

## 6. قائمة التوقيع (Sign-off)

| الدور | الاسم | التاريخ | التوقيع |
|-------|-------|---------|---------|
| Data Platform | | | |
| Security | | | |
| Data Governance / Legal | | | |
| مالك جهة (مثال MOF) | | | |
| مالك جهة (مثال MOH) | | | |

المرفقات الإلزامية للإصدار:

- `assign-*.json` و`reconcile-after.json`
- تقرير اختبارات CrossTenant (وحدة + تكامل)
- قائمة الحجر المفتوحة مع خطة القرار

## 7. ملاحظات تشغيلية

- لا تُشغّل الإسناد على الإنتاج قبل نجاح dry-run على نسخة حديثة من staging
- مشغّل المنصة (`platform-admin`) فقط يتجاوز مرشحات EF عبر `IgnoreTenantFilter`
- جداول المنصة المستثناة من الإسناد: `Tenants`، `TenantAssignmentRules`، `TenantQuarantineEntries`، outbox، audit العالمي
