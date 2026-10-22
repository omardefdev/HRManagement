# PLAN-27-EXEC — سلامة خطاب العرض ومبدأ الجدارة وقائمة الاحتياط (HCM-E)

تنفيذ [PLAN-27](../PLAN-27-hcm-offer-merit-reserve.md) لإثبات **AC-HCM-27a..h** (الجلستان 1 و2).

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1` — صورتا `hr-core-web` و`payroll-web` مبنيتان من الشيفرة الحالية (تُطبَّق ترحيلات `HCM027_*` عند إقلاع HR.Core؛ Payroll بلا ترحيل جديد).
- **Keycloak:** يجب إعادة مزامنة الـ realm بعد هذه الخطة لأن الاستيراد يعمل عند الإقلاع الأول فقط:
  - `pwsh scripts/prepare-keycloak-import.ps1 -Profile Acceptance` ثم `pwsh scripts/sync-keycloak-dev-realm.ps1 -ContainerName <keycloak>` (يستدعيه `acceptance-up.ps1` تلقائياً).
  - المزامنة تُنشئ الشخصية الجديدة `a-management-approver-2` وتضيف النطاق `payroll.grades.read` (جمهور `api-payroll`) كنطاق اختياري للعميل `svc-hr-core`.
- **الشخصية الجديدة:** `a-management-approver-2` / `acc-a-management-approver-2` (الدور `hr-management-approver`، الجهة A). كلمة المرور اختيارية عبر متغير البيئة **`ACCEPTANCE_A_MANAGEMENT_APPROVER_2_PASSWORD`**، وإلا يولّدها `prepare-keycloak-import.ps1` في `.acceptance/credentials.json` (غير متتبَّع في git).
- **الاتصال HR.Core → Payroll (S2S):** في `docker-compose.acceptance.yml` و`docker-compose.local.yml` فقط:
  `Integration__PayrollBaseUrl=http://payroll-web:8080`، `ServiceIdentity__ClientSecret=${SVC_HR_CORE_CLIENT_SECRET:-dev-svc-secret-local-only}`، `ServiceIdentity__Scope=file-storage.references payroll.grades.read`.
  يجب أن يطابق السر قيمة العميل `svc-hr-core` في Keycloak (`sync-keycloak-dev-realm.ps1 -SvcSecret`). بلا `Integration:PayrollBaseUrl` تُعدّ الرواتب «غير متاحة» ويلزم مبرر الراتب (عرض استثنائي).
- `NationalScope:IsLabScale=true` (اعتماد طلب الشاغر والعرض بالمسار).
- إعدادات `Recruitment` (القيم الافتراضية):

| المفتاح | الافتراضي | الجلسة |
|---------|-----------|--------|
| `OfferValidityDays` | 14 (المقبول 3..60) | 1 |
| `Jobs:OfferExpiryEnabled` / `Jobs:OfferExpiryIntervalMinutes` | `true` / 60 | 1 |
| `MaxStartDays` | 90 | 2 |
| `ReserveValidityMonths` | 6 (القرار D-7) | 2 |
| `AutoDraftNextOffer` | `false` | 2 |
| `Jobs:ReserveExpiryEnabled` / `Jobs:ReserveExpiryIntervalMinutes` | `true` / 1440 | 2 |
| `RejectSiblingApplicationsOnHire` | `true` (عند `false` لا يتغير المنافسون إطلاقاً) | قائم |

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| A | أهلية العرض وترتيب الجدارة | `OfferEligibilityRules`, `RecruitmentOfferService.CreateDraftAsync/GetBestAvailableRankAsync` |
| B | مسار الاستثناء بثلاثة معتمدين مختلفين | `OfferOverrideApprovalRules`, `WorkflowPackSeeder` (`RECRUITMENT-OFFER-OVERRIDE-APPROVAL`), `R-OFF-OVERRIDE`/`R-OFF-EXCEPTION`, `WorkflowInstanceActionService` |
| C | الرفض والإلغاء والانتهاء | `RecruitmentOfferService.DeclineAsync/RevokeAsync/ExpireAsync`, حالتا `OfferDeclined`/`ReserveListed` |
| D | مهمة انتهاء العروض | `RecruitmentOfferExpirer`, `RecruitmentOfferExpiryJob` (قفل 8675313) |
| E | اتساق شروط العرض | `OfferTermsRules`, `IPayrollGradeLookup`/`HttpPayrollGradeLookup`/`UnavailablePayrollGradeLookup`, `PayGradeLookupInternalController` (Payroll), `ServiceScopes.PayrollGradesRead`, `ServicePolicies.PayrollGradeLookup` |
| F | قائمة الاحتياط | `ReserveListService`, `RecruitmentHireClosureService`, `RecruitmentReserveExpirer`, `RecruitmentReserveExpiryJob` (قفل 8675314), `_NextCandidateBanner` |
| G/H | الاختبارات والتوثيق | `Hcm027*`, `HttpPayrollGradeLookupTests`, `Hcm027PayGradeLookupIntegrationTests`, `AcHcm27OfferMeritTests`, `hcm027-validate.ps1` |

الترحيلات (HR.Core): `HCM027_OfferOutcome`، `HCM027_OfferTerms`، `HCM027_ReserveList` — أعمدة جديدة فقط، بلا خطوة بيانات.

## أوامر التحقق

```powershell
powershell -File scripts/hcm027-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm027|FullyQualifiedName~HttpPayrollGradeLookup"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm027|FullyQualifiedName~RecruitmentHireClosure"   # Hcm027 يحتاج Docker (Testcontainers)
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm27
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| كل السيناريوهات | `central-a` → `br1-mgr` → `a-management-approver` | إنشاء طلب الشاغر واعتماده؛ صانع العرض = `central-a` |
| AC-HCM-27a/d/e/f | `br1-mgr` (خطوة 1) + `a-management-approver` (خطوة 2) | `RECRUITMENT-OFFER-APPROVAL` |
| AC-HCM-27b | `br1-mgr` → `a-management-approver` → `a-management-approver-2` | `RECRUITMENT-OFFER-OVERRIDE-APPROVAL` بثلاثة معتمدين مختلفين |
| AC-HCM-27c/h | `central-a` | حدود الراتب، الإلغاء بخطأ إداري |
| AC-HCM-27g | (مهمة خلفية) | انتهاء قائمة الاحتياط |

> الخطوة 3 من مسار الاستثناء تُسند إلى `hr-management-approver` بمعتمد مختلف إلى حين تنفيذ PLAN-25 (ستنتقل إلى `compliance`).

## Manual — عرض بالجدارة وحدود الراتب (AC-HCM-27a/c)

1. `acc-central-a` → `RecruitmentApplications/Details` لمرشح «مُرتَّب» برتبة 1: بطاقة «إنشاء خطاب عرض» تُظهر **الدرجة** مملوءة من المنصب الممول («من المنصب الممول») وتنبيه «حدود الدرجة G… في سلّم الرواتب الساري: الحد الأدنى–الحد الأقصى».
2. سالب (c): راتب أعلى من الحد الأقصى → رفض «الراتب المعروض خارج حدود الدرجة {الدرجة}: {الأدنى}–{الأقصى}.»
3. سالب: درجة غير معرّفة في السلّم الساري → «الدرجة {الدرجة} غير معرّفة في سلّم الرواتب الساري.»
4. سالب (a): مرشح «مُقيَّم» غير مرتب → لا تظهر بطاقة العرض، وإن أُرسل الطلب يُرفض «يمكن إصدار العرض للمرشحين المرتبين فقط.»
5. سالب (E4): تاريخ مباشرة قبل اليوم → «تاريخ المباشرة المقترح لا يجوز أن يكون قبل اليوم.»؛ بعد أكثر من 90 يوماً → «تاريخ المباشرة المقترح يجب ألا يتجاوز 90 يوماً من اليوم.»
6. موجب: راتب ضمن الحدود → «إنشاء مسودة» → `OfferLetters/Details` يُظهر «حدود الدرجة» ووقت التحقق → **تقديم للاعتماد** → `acc-br1-mgr` اعتماد → `acc-a-management-approver` اعتماد → «صادرة» مع تاريخ انتهاء = الإصدار + مدة الصلاحية.

## Manual — عرض استثنائي (AC-HCM-27b)

1. سالب: عرض لصاحب الرتبة 2 بلا تجاوز → «المرشح ليس الأعلى رتبة المتاح (الرتبة المتاحة: 1).»
2. موجب: تفعيل «تجاوز الجدارة» + مبرر (30 حرفاً على الأقل) → العرض يحمل شارة «عرض استثنائي — تجاوز الجدارة».
3. أسباب أخرى تجعل العرض استثنائياً (الجلسة 2): **اختلاف الدرجة** عن درجة المنصب يتطلب «مبرر اختلاف الدرجة» (10 أحرف) → شارة «عرض استثنائي — اختلاف الدرجة»؛ و**تعذّر الوصول إلى الرواتب** يتطلب «مبرر الراتب» (20 حرفاً) → شارة «راتب غير متحقق منه».
4. **تقديم للاعتماد** → مسار `RECRUITMENT-OFFER-OVERRIDE-APPROVAL`: «مراجعة HR» (`acc-br1-mgr`) ← «اعتماد إداري» (`acc-a-management-approver`) ← «اعتماد استثناء الجدارة» (`acc-a-management-approver-2`).
5. سالب: المعتمد نفسه في الخطوتين 2 و3 → «لا يجوز لنفس المعتمد اعتماد أكثر من خطوة في عرض استثنائي.»
6. `OfferLetters/Index` → «عروض استثنائية فقط» يعرض عروض الجدارة/الدرجة/الراتب غير المتحقق.

## Manual — رفض العرض والمرشح التالي والإلغاء (AC-HCM-27d/h)

1. عرض «صادر» → بطاقة «رفض العرض» → سبب (5 أحرف) → «تسجيل رفض المرشح» (HR نيابة عن المرشح) → العرض «مرفوض» والطلب «عرض مرفوض/منتهٍ».
2. `RecruitmentRequisitions/Details` و`OfferLetters/Details` تعرضان لافتة «المرشح التالي بالجدارة: {الاسم} (رتبة n) — إنشاء عرض» (تظهر فقط إن كان الطلب «منشور» ولا عرض نشط عليه).
3. «إنشاء عرض» يفتح بطاقة العرض لذلك المرشح؛ يُنشأ العرض دون تجاوز جدارة.
4. مع `Recruitment:AutoDraftNextOffer=true` تُنشأ المسودة آلياً بالدرجة والراتب والصلاحية نفسها (وتمر بالتحققات نفسها) ويظهر ذلك في رسالة النجاح.
5. (h) بطاقة «إلغاء العرض» → سبب (10 أحرف) + «خطأ إداري — إعادة المرشح إلى المرتبين» → الطلب يعود «مُرتَّب»؛ دون الخيار → «مرفوض».

## Manual — انتهاء العرض (AC-HCM-27e)

1. عرض «صادر» تجاوز تاريخ انتهائه: **قبول العرض** يُرفض «انتهت صلاحية العرض ولا يمكن قبوله.»
2. مهمة `RecruitmentOfferExpiryJob` (كل 60 دقيقة) → العرض «منتهي الصلاحية» والطلب «عرض مرفوض/منتهٍ» مع ملاحظة «انتهاء صلاحية العرض»، ثم تظهر لافتة المرشح التالي.

## Manual — التعيين وقائمة الاحتياط (AC-HCM-27f/g)

1. قبول عرض الرتبة 1 → **تعيين** (`RecruitmentApplications/Hire`).
2. المنافسون «مُرتَّب» → «قائمة احتياط» مع «حتى {اليوم + 6 أشهر}» في تفاصيل الطلب وقائمة الطلبات ولوحة الترتيب؛ غير المرتبين → «مرفوض». (بخيار `RejectSiblingApplicationsOnHire=false` لا يتغير أحد.)
3. مهمة `RecruitmentReserveExpiryJob` (يومياً) → كل «قائمة احتياط» تجاوزت تاريخها → «مرفوض» مع ملاحظة «انتهاء صلاحية قائمة الاحتياط».

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-HCM-27 | `acceptance-evidence/AC-HCM-27/` | `steps-27a-c-d-h.md`, `steps-27b.md`, `steps-27e.md`, `steps-27f-g.md`, `http/*.json`, `db-checks.json` (حالات الطلبات والعروض ومسار الاعتماد والرسائل المتوقعة)، `timing.json` (`offerExpirySeconds`, `reserveExpirySeconds`) |

> ترتيب المرشحين (AC-HCM-26) يُضبط في اختبارات AC-HCM-27 بشرط مسبق موثّق عبر SQL؛ والسفر الزمني (تاريخ انتهاء العرض، تاريخ الاحتياط) كذلك. رسائل TempData لا تصل عبر البوابة، لذا تُثبت الحالات السالبة بفحص قاعدة البيانات.
> إن تعذّر على HR.Core الوصول إلى Payroll (سر أو نطاق غير مطابق) تُتخطّى سيناريوهات القبول بـ `Skip.If` مع السبب.

## Regression

- `Hcm021` / `Hcm022` / `Hcm026` / `Fun001` / `Syr001` / `RecruitmentHireClosure` يجب أن تبقى PASS (باستثناء الإخفاقات السابقة الموثقة في تسليم PLAN-26).
- لا تغيير على `WorkflowStageGuard` / `WorkflowEngine` (PLAN-25 مؤجلة).
