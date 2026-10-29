# PLAN-25-EXEC — اعتمادات التوظيف بإعدادات الإنتاج وفرضها في الخادم (HCM-025)

تنفيذ [PLAN-25](../PLAN-25-hcm-production-workflow-binding.md) لإثبات **AC-HCM-25a..h**. سجل التقدم والقرارات:
[`reports/plan25-progress.md`](../../../reports/plan25-progress.md)، والتقرير الختامي
[`reports/plan25-final-report.md`](../../../reports/plan25-final-report.md)، وخط الأساس
[`reports/plan25-baseline.md`](../../../reports/plan25-baseline.md). موجّه التنفيذ: [PLAN-25-AGENT-PROMPT.md](PLAN-25-AGENT-PROMPT.md).

## ما الذي تغيّر

- **بدء المسار دائماً (G-02):** `ApprovalStartCoordinator` لتقديم طلب الشاغر والعرض (العادي والتجاوز) وجلسة اللجنة وطلب تغيير
  الملاك. الحالة «بانتظار الاعتماد» (أو تقديم اللجنة) لا تُحفظ إلا مع نسخة المسار. لا تعريف منشور ← «لا يوجد تعريف مسار اعتماد
  منشور لهذا الإجراء.» دون تغيير. طلب تغيير الملاك لم يعد يُطبَّق فوراً خارج المختبر.
- **فحص الدور والبوابات في الخادم (G-04):** `WorkflowStageGuard` + بوابات الكيان في الاعتماد والرفض، والواجهة تسأل الفحص نفسه.
- **تقاعد RECRUITMENT-HIRE (G-14):** القاعدة `R-REC-HIRE` معطّلة، والتعريف غير منشور، والنسخ اليتيمة تُلغى بسكربت الإصلاح.
- **سياسات الكتابة والنطاق (G-15، D10):** سياسات صريحة على الإعلانات والشواغر الداخلية، ونطاق الفرع على شاشات التوظيف.

## الإعدادات حسب البيئة

| المفتاح | المختبر/القبول (`appsettings.json`) | Staging / Production | الملف الشبيه بالإنتاج |
|---------|--------------------------------------|----------------------|------------------------|
| `NationalScope:IsLabScale` | `true` | `false` | `false` |
| `PolicyEngine:Core.Recruitment` | غير مضبوط (Off) | `Shadow` | `Shadow` |
| `Recruitment:Approvals:AlwaysStartWorkflow` | `true` (افتراضي) | `true` | `true` |
| `Recruitment:Approvals:FailIfDefinitionMissing` | `true` (افتراضي) | `true` | `true` |
| `Workflow:AdminBypass` | `false` | `false` | `false` |
| `Workflow:EnforceStepRoleEntityTypes` | أنواع التوظيف السبعة + الملاك | نفسها | نفسها |

`docker-compose.acceptance-prodlike.yml` يبقي `ASPNETCORE_ENVIRONMENT=Acceptance` (الشخصيات وبيانات القبول وFileDrop)، فلا يُحمَّل
`appsettings.Production.json`؛ لذلك يكرّر قيم الإنتاج صراحةً.

## المتطلبات

- **الصور:** تتغير `hr-core-web` فقط (و`HRManagement.ServiceDefaults` المشترك دون أثر وظيفي على الخدمات الأخرى). لا ترحيلات
  جديدة، ولا تغيير على Keycloak أو الشخصيات أو البوابة. التصحيحات عند الإقلاع: `WorkflowPackSeeder.RetireAsync` و
  `PolicyEngineDomainSeeds.DeactivateRuleIfPresentAsync("R-REC-HIRE")`.
- **إعادة بناء hr-core-web على مكدّس قائم** (PowerShell من جذر المستودع):

  ```powershell
  . ./scripts/acceptance-local-env.ps1; Import-AcceptanceLocalEnvironment -RepoRoot (Get-Location).Path
  $f = "-f docker-compose.yml -f docker-compose.services.yml -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml -f docker-compose.acceptance.yml".Split(' ')
  docker compose -p hrm-acceptance @f --profile full-stack build hr-core-web
  ```

  ثم مكدّس نظيف (تُعاد الحالات المبذورة لـ PLAN-33 التي تستهلكها السيناريوهات) — من bash أو cmd، لا بإعادة توجيه `*>` في
  PowerShell 5.1 (تحوّل رسائل docker على stderr إلى أخطاء):

  ```powershell
  powershell -ExecutionPolicy Bypass -File scripts/acceptance-reset.ps1
  powershell -ExecutionPolicy Bypass -File scripts/acceptance-up.ps1 -NoBuild -SkipLiveVerification
  ```

- **التحويل إلى الملف الشبيه بالإنتاج** (ثم العودة بإعادة الإنشاء دون الملف الأخير):

  ```powershell
  docker compose -p hrm-acceptance @f -f docker-compose.acceptance-prodlike.yml --profile full-stack up -d --no-deps --no-build --force-recreate --wait hr-core-web hr-contracts-web
  ```

- **سر عميل البوابة للاختبارات:** `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET` من `.env` مع `RUN_ACCEPTANCE` و`GATEWAY_BASE_URL`
  (`http://localhost:6200`) و`KEYCLOAK_BASE_URL` (`http://localhost:6388`).

## الأوامر

```powershell
powershell -ExecutionPolicy Bypass -File scripts/hcm025-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm025|FullyQualifiedName~WorkflowStageGuard|FullyQualifiedName~Architecture"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm025|FullyQualifiedName~Hcm033LabScale|FullyQualifiedName~Est00"
# على الملف الشبيه بالإنتاج:
$env:RUN_ACCEPTANCE = "true"; $env:ACCEPTANCE_REQUIRE_ALL = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "FullyQualifiedName~AcHcm25|FullyQualifiedName~AcHcm21|FullyQualifiedName~AcHcm22|FullyQualifiedName~AcWf23|FullyQualifiedName~AcHcm26|FullyQualifiedName~AcHcm27|FullyQualifiedName~AcHcm28|FullyQualifiedName~AcHcm29|FullyQualifiedName~AcHcm30|FullyQualifiedName~AcHcm31|FullyQualifiedName~AcHcm32|FullyQualifiedName~AcHcm33"
$env:RUN_PLAYWRIGHT_E2E = "true"; dotnet test src/Tests/HRManagement.E2E --filter "Category=JourneyProdLike"
```

الأدلة في `acceptance-evidence/AC-HCM-25/` (metadata، steps، before، after، audit، db-checks، timing، result، http)، وتقارير
سكربت الإصلاح في `acceptance-evidence/AC-HCM-25/repair/`.

## سكربت الإصلاح

`scripts/hcm025-repair-stuck-approvals.ps1` يستدعي `POST /api/hr-core/api/recruitment/admin/repair-approvals?dryRun=true|false`
عبر البوابة (سياسة `HrCore.Delete` ← `hr-admin`):

- **تشغيل تجريبي (افتراضي):** يسرد طلبات الشاغر والعروض «بانتظار الاعتماد» دون مسار، وجلسات اللجنة المقدّمة دون مسار،
  والاستئنافات المعلّقة ذات المراجع المعيّن دون مسار (وليست بانتظار معلومات)، وطلبات تغيير الملاك العالقة، ونسخ
  `RECRUITMENT-HIRE` قيد التنفيذ. لا يغيّر شيئاً.
- **`-Apply`:** يبدأ المسار الناقص لكل سجل عبر المنسّق نفسه (سجل تدقيق `ApprovalWorkflowRestarted`)، ويلغي النسخ اليتيمة (سجل
  `OrphanHireWorkflowCancelled`). تعريف مفقود ← السطر `Failed` مع الرسالة، دون إيقاف التشغيل. تشغيل ثانٍ لا يجد شيئاً.
- **بيانات الدخول لا تُخزَّن:** `-Username`/`-Password` أو `HCM025_ADMIN_USERNAME`/`HCM025_ADMIN_PASSWORD`؛ على مكدّس القبول
  المحلي فقط تُقرأ كلمة مرور الشخصية من `.acceptance/credentials.json` (غير متتبَّع) وسر العميل من `.env`. المخرجات: CSV وJSON في
  `artifacts/hcm025-repair/`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/hcm025-repair-stuck-approvals.ps1            # تجريبي
powershell -ExecutionPolicy Bypass -File scripts/hcm025-repair-stuck-approvals.ps1 -Apply     # إصلاح
```

## الشخصيات

| الشخصية | الدور | الاستعمال |
|---------|-------|-----------|
| `acc-central-a` | `hr-core` | المنشئ: طلبات الشاغر، العروض، الجلسات، الاستئنافات |
| `acc-br1-mgr` | `branch-manager` + `hr-core` (نطاق ريف دمشق) | الخطوة 1 والخطوة 2 للجنة؛ رفض الدور في الخطوة 2 (25c) |
| `acc-a-management-approver` | `hr-management-approver` | الخطوة 2 (و3)؛ 403 على تعديل الإعلان (25f) |
| `acc-a-compliance` / `acc-a-compliance-2` | `compliance` | المراجع المعيّن / غير المعيّن (25e) |
| `acc-a-directory-checker` | `hr-admin` | سكربت الإصلاح (25h) |

## خطوات يدوية (للتحقق من الواجهة)

1. **بدء المسار بإعدادات الإنتاج:** بعد التحويل إلى الملف الشبيه بالإنتاج، ادخل بـ `acc-central-a`، أنشئ طلب شاغر على منصب من
   `AC33-POOL-*` وقدّمه: تظهر لوحة «مسار الاعتماد» بالخطوة الأولى، ولا يظهر أي تنبيه عن مسار مفقود.
2. **فحص الدور في الخادم:** ادخل بـ `acc-a-management-approver` وافتح الطلب في الخطوة 1: يظهر «هذه الخطوة تتطلب دور «hr-core».»
   ولا يظهر نموذج الاعتماد؛ ولو أُرسل الطلب مباشرة فلن تتقدم الخطوة.
3. **الاعتماد:** `acc-br1-mgr` يعتمد الخطوة 1، ثم `acc-a-management-approver` الخطوة 2 ← «معتمد».
4. **نطاق الفرع:** بـ `acc-br1-mgr` افتح «طلبات الشاغر»: تظهر طلبات ريف دمشق فقط؛ فتح طلب من فرع آخر بعنوانه المباشر ← 404؛
   «المسابقات» ← 403.
5. **تعديل الإعلان:** بـ `acc-a-management-approver` افتح إعلاناً عاماً: يُعرض، لكن «تعديل» ← 403.
6. **إعادة بدء مسار عالق:** معاملة بقيت «بانتظار الاعتماد» دون مسار (من إصدار سابق) تُظهر التنبيه وزر «إعادة بدء مسار الاعتماد»
   لـ `hr-core`؛ بعد الضغط تظهر لوحة المسار.

## ما يبقى خارج النطاق

فرض دور الخطوة على النطاقات الأخرى (العهدة، الفقدان، النقل…)، وسياسات الكتابة على الـ 42 إجراءً المجمّدة في
`tests/architecture/hrcore-post-policy-allowlist.txt`، ونطاق الفرع على `PromoteFromCitizen`/`PromoteFromInternal` والشواغر
الداخلية، وتشغيل CI (الفرع غير مدفوع) — انظر التقرير الختامي §11.
