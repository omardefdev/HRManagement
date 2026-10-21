# PLAN-08 — تهيئة الجهة + الحجر والمصالحة (AC-07, AC-08)

**التغطية:** AC-07, AC-08. **الطبقة:** 4. **يعتمد على:** PLAN-00.

## الهدف

إثبات تهيئة جهة جديدة من Agency Manifest وحزمة البيانات الوطنية (إنشاء الجهة والوحدات والبيانات المرجعية مرة واحدة دون ازدواج عند الإعادة)، وإثبات دخول السجل غير المحسوم إلى الحجر ومنع تشغيله ثم خروجه بعد المصالحة مع توثيق القرار.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ (AC-07): [AgencyProvisioningService.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Onboarding/AgencyProvisioningService.cs) + `AgencyManifestLoader` + [OnboardingController.cs](../../src/Services/HRCore/HR.Core.Web/Controllers/OnboardingController.cs) (`apply?dryRun`)؛ لا-ازدواج عبر `SeedPackApplication` بمفتاح `(TenantId, PackId, ReleaseDigest)`؛ نسخ MDM يتخطى الموجود؛ مثال [seed-packs/examples/agency-ministry-finance.json](../../seed-packs/examples/agency-ministry-finance.json).
- فجوة **AC-07**: **لا إنشاء وحدات تنظيمية** في خط التهيئة (جهة+MDM+سياسات فقط)؛ لا حدث `AgencyOnboarded`؛ لا تأكيد صريح لعدم الازدواج عند إعادة تطبيق جهة.
- منفّذ (AC-08، بيانات/سكربتات): [TenantQuarantineEntry.cs](../../src/Shared/HRManagement.SharedKernel/Tenancy/TenantQuarantineEntry.cs) + [TenantAssignmentRule.cs](../../src/Shared/HRManagement.SharedKernel/Tenancy/TenantAssignmentRule.cs)؛ [scripts/assign-tenants.ps1](../../scripts/assign-tenants.ps1)، [scripts/reconcile-tenants.ps1](../../scripts/reconcile-tenants.ps1)؛ [docs/TENANT_MIGRATION_RUNBOOK.md](../TENANT_MIGRATION_RUNBOOK.md).
- فجوة **AC-08**: الحجر **ليس حارساً تشغيلياً** (لا middleware يمنع العمليات على السجلات المحجورة)؛ لا API لحلّ الحجر يكتب `Resolution/ResolvedAt/AssignedTenantId`.

## تغييرات التنفيذ (مهام)

1. **AC-07:** إضافة إنشاء شجرة الوحدات في `AgencyProvisioningService`؛ نشر حدث `AgencyOnboarded`؛ تأكيد صريح لعدم الازدواج عند الإعادة (اختبار re-apply). هذا يجعل بذرة PLAN-00 تُثبت AC-07 نفسها.
2. **AC-08:** حارس تشغيلي يمنع العمليات على السجلات المحجورة/غير المحسومة؛ API حلّ الحجر (إسناد/إبقاء/تراجع) يكتب القرار وسببه؛ ربط بتقرير المصالحة قبل/بعد.

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-07 | تهيئة جهة ⇒ إنشاء الجهة والوحدات والبيانات المرجعية | إعادة التطبيق لا تُنشئ ازدواجاً |
| AC-08 | سجل غير محسوم يدخل الحجر ثم يخرج بعد مصالحة موثّقة | تشغيل السجل المحجور ممنوع حتى الحل |

## منهج الاختبار

مجموعة `Ac07..Ac08`؛ إعادة استخدام `AgencyProvisioningTests`, `NationalSeedTests`، وتشغيل سكربتات الإسناد/المصالحة على بيانات القبول.

## مخرجات الأدلة

`acceptance-evidence/AC-07/`: استجابة apply + إعادة apply + عدّ الكيانات (لا ازدواج). `AC-08/`: محاولة تشغيل محجور (ممنوعة) + تقرير مصالحة قبل/بعد + قرار الحل.

## تعريف الإنجاز

التهيئة idempotent وتُنشئ الوحدات وتُطلق الحدث؛ الحجر يمنع التشغيل ويُحَل بقرار موثّق؛ AC-07/AC-08 = `Pass`.
