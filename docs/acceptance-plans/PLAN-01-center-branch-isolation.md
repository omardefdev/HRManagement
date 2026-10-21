# PLAN-01 — عزل المركز والفروع (AC-01..AC-06)

**التغطية:** AC-01, AC-02, AC-03, AC-04, AC-05, AC-06. **الطبقة:** 1 (أولوية قصوى). **يعتمد على:** PLAN-00.

## الهدف

إثبات العزل بين الجهات (Tenant) وبين الفروع داخل الجهة (OrgScope) بالطبقات الأربع (بوابة/خدمة/استعلام/RLS)، وتجاوز مسؤول المنصة المدقّق، والنقل بين الفروع مع انقلاب الوصول وحفظ التاريخ.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: عزل الجهة عبر [TenantAwareDbContext.cs](../../src/Shared/HRManagement.ServiceDefaults/TenantAwareDbContext.cs) + [TenantQueryFilterExtensions.cs](../../src/Shared/HRManagement.ServiceDefaults/TenantQueryFilterExtensions.cs) + RLS في [TenantRlsMigrationHelper.cs](../../src/Shared/HRManagement.ServiceDefaults/TenantRlsMigrationHelper.cs) مع [NpgsqlTenantSessionInterceptor.cs](../../src/Shared/HRManagement.ServiceDefaults/NpgsqlTenantSessionInterceptor.cs)؛ نطاق الفرع عبر [OrgScopeService.cs](../../src/Services/HRCore/HR.Core.Infrastructure/OrgScopeService.cs) + [HrOrgScopeVisibility.cs](../../src/Shared/HRManagement.ServiceDefaults/HrOrgScopeVisibility.cs) + [OrgScopeControllerExtensions.cs](../../src/Shared/HRManagement.ServiceDefaults/OrgScopeControllerExtensions.cs).
- فجوة **AC-03**: تدقيق رفض النطاق عام (لا جهة/مستخدم/معرّف/سبب) في [GatewayAuditMiddleware.cs](../../src/Shared/HRManagement.ServiceDefaults/GatewayAuditMiddleware.cs) (`GatewayAuditEntry` بلا TenantId/Reason).
- فجوة **AC-04**: البوابة لا ترفض `tenant_id` المفقود/التالف ([GatewayPathAuthorizationMiddleware.cs](../../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs) دور فقط).
- فجوة **AC-05**: تجاوز `platform-admin` عام (`BypassTenantFilter` يرى كل الجهات)، لا "دخول جهة محددة بسبب + تدقيق".
- فجوة **AC-06**: `TransferExecutedConsumer` في HR.Core لا يُغلق `EmploymentRecord.EndDate` السابق؛ لا اختبار لانقلاب الوصول. و`OrgScopeAuthorizationHandler` غير مُسجَّل في أي `Program.cs`.

## تغييرات التنفيذ (مهام)

1. **AC-03:** إثراء التدقيق عند رفض النطاق: انبعاث `SecurityAuditEvent` (جهة/مستخدم/مورد/سبب) من مسارات `Forbid` في `OrgScopeControllerExtensions` و`EmployeeOrgScopeGuard`، وإضافة حقول Tenant/Scope/Reason إلى `GatewayAuditEntry`.
2. **AC-04:** رفض `tenant_id` مفقود/تالف عند البوابة (401/403) في `GatewayPathAuthorizationMiddleware`، مع سكربت إثبات يمرّ بالطبقات الأربع.
3. **AC-05:** مسار "دخول جهة" يتطلب سبباً، يقصر الجلسة على جهة واحدة، يكتب تدقيقاً (سبب/جهة/مستخدم)، ويضبط `app.current_tenant` لتلك الجهة بدل التجاوز العام. تعديل `TenantContext`/`TenantAwareDbContext` وإضافة متحكم/إجراء مخصص.
4. **AC-06:** إغلاق `EmploymentRecord.EndDate` السابق وحفظ تاريخ النقل في `TransferExecutedConsumer`؛ تسجيل `OrgScopeAuthorizationHandler` أو حذفه إن اكتُفي بالحراسة اليدوية.

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-01 | المركزي يبحث ويرى موظفي فرع1 وفرع2 (Tenant-A) | لا يظهر أي سجل من Tenant-B |
| AC-02 | مدير فرع1 يرى فرع1 + قسمه | لا يرى فرع2 الشقيق |
| AC-03 | — | طلب موظف شقيق عبر رابط مباشر + API ⇒ 403/404، صفر تسريب، وتسجيل المحاولة |
| AC-04 | — | تخمين معرف موظف من جهة أخرى ⇒ منع بالبوابة والخدمة والاستعلام وRLS |
| AC-05 | `platform-admin` يدخل جهة محددة بسبب ⇒ سجل تدقيق (سبب/جهة/مستخدم) | لا يتصفح كل الجهات صامتاً بلا سبب؛ ولا يمنح مستخدم جهة هذا التجاوز |
| AC-06 | نقل موظف فرع1←فرع2 ⇒ مدير فرع2 يصل، والتاريخ محفوظ | مدير فرع1 يفقد الوصول بعد النقل |

## منهج الاختبار

مجموعة قبول جديدة `Ac01..Ac06` عبر البوابة بشخصيات PLAN-00. يُعاد استخدام [CrossTenantRlsIntegrationTests](../../src/Tests/HRManagement.IntegrationTests/) (RLS على 14 قاعدة، `RUN_TESTCONTAINERS=true`)، و`HrOrgScopeVisibilityTests`، و`Iam002OrgScopeTests`، واختبارات مستهلك النقل.

## مخرجات الأدلة

`acceptance-evidence/AC-01..AC-06/` كاملة: طلب/استجابة/HTTP، لقطات قبل/بعد (خصوصاً AC-06)، `audit.json` مع سبب الرفض وCorrelationId (AC-03/AC-05)، وفحوص RLS/DB (AC-04).

## تعريف الإنجاز

الطبقات الأربع تمنع العابر؛ تدقيق AC-03/AC-05 يحمل الجهة/المستخدم/السبب؛ النقل يقلب الوصول ويحفظ التاريخ؛ كل AC-01..06 = `Pass` بأدلة كاملة.
