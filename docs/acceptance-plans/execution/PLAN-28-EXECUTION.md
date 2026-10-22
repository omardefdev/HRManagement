# PLAN-28-EXEC — حوكمة لجنة الاختيار والاستئناف (HCM-F)

تنفيذ [PLAN-28](../PLAN-28-hcm-committee-appeal-governance.md) لإثبات **AC-HCM-28a..i** (الجلستان 1 و2).

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1` — صورتا `hr-core-web` و`hr-gateway` مبنيتان من الشيفرة الحالية
  (تُطبَّق ترحيلات `HCM028_*` عند إقلاع HR.Core؛ قواعد مسارات البوابة الجديدة في `HRManagement.ServiceDefaults`).
- **إعادة تهيئة بيئة القبول:** تغيّرت بذور القبول (طلب الشاغر الخاضع للجنة في الجلسة 1، والشخصية الجديدة في الجلسة 2)،
  فصورة `hr-core-web` من هذا الفرع ترفض قاعدة قبول قديمة. نفّذ `pwsh scripts/acceptance-reset.ps1` ثم
  `pwsh scripts/acceptance-up.ps1` (يحذف بيانات مشروع `hrm-acceptance` فقط). البذرة **تُستهلك مرة واحدة**: AC-HCM-28a
  يتخطى نفسه إن كانت الجلسة المبذورة قد قُدّمت، فأعد التهيئة قبل إعادة التشغيل.
- **Keycloak:** الاستيراد يعمل عند الإقلاع الأول فقط؛ `acceptance-up.ps1` يستدعي
  `prepare-keycloak-import.ps1 -Profile Acceptance` و`sync-keycloak-dev-realm.ps1` فتُنشأ الشخصية الجديدة.
- **الشخصية الجديدة:** `a-compliance-2` / `acc-a-compliance-2` (الدور `compliance`، الجهة A، موظف HQ ترتيبه 40 — ليس عضواً
  في لجنة البذرة). كلمة المرور اختيارية عبر متغير البيئة **`ACCEPTANCE_A_COMPLIANCE_2_PASSWORD`**، وإلا يولّدها
  `prepare-keycloak-import.ps1` في `.acceptance/credentials.json` (غير متتبَّع في git). عدد الشخصيات الآن 57 (الجهة A: 39).
- مطالبة `employee_id` في الرمز إلزامية لعضو اللجنة والمراجع المستقل (تحملها شخصيات القبول).
- AC-HCM-28h يحتاج اتصال HR.Core → Payroll لحدود الدرجة (كما في [PLAN-27-EXECUTION](PLAN-27-EXECUTION.md))، وإلا يُتخطى.
- إعدادات `Recruitment` (القيم الافتراضية):

| المفتاح | الافتراضي | الجلسة |
|---------|-----------|--------|
| `CommitteeApprovalDefault` | `false` (و`true` في Production/Staging) | 1 |
| `AppealWindowDays` | 14 (من `RejectedAt`) | 2 |
| `AppealableRejectionKinds` | `SelectionDecision`, `NotSelectedOnHire`, `OfferRevoked` | 2 |
| `BlockHireOnPendingAppeal` | `true` | 2 |

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| A | نموذج اللجنة (الغرض، الرئيس، التعارض لكل مرشح، الأصوات، التوصيات، مفتاح الطلب) | `SelectionCommitteeSession/Vote/Decision/MemberCoi`, `RecruitmentRequisition.RequiresCommitteeApproval` |
| B | قفل التقديم وفحوص الاعتماد على الخادم | `SelectionCommitteeRules`, `SelectionCommitteeLifecycleService`, `WorkflowInstanceActionService` |
| C | بوابة الترتيب وتطبيق ترتيب اللجنة | `RecruitmentSelectionService.AssignRankAsync/ApplyCommitteeRankingAsync`, `CommitteeRankingGate` |
| D | محضر اللجنة PDF | `SelectionCommitteeMinutesService`, `SelectionCommitteeMinutesDocument` |
| E | بذرة القبول | `AcceptanceDataSeeder.SeedCommitteeGatedRequisitionAsync` (`REQ-2026-A-CMT1`، فرع RIF) |
| F | شريحة وصول `compliance` | `HrCoreAppealReview`, `HrCoreWorkInbox`, `HrModuleAuthorizationFilter`, `GatewayPathAuthorizationMiddleware`, `RecruitmentAppealsController`, `RecruitmentAppealAccess` |
| G | نوع الرفض وتاريخه ومهلة الاستئناف | `ApplicationRejection`, `RecruitmentRejectionKind`, `RecruitmentAppealWorkflowRules.GetAppealError` |
| H | طريق استئناف واحد | `RecruitmentAppealService`, `RecruitmentAppealWorkflowHandler`, `RecruitmentAppealOutcome` |
| I | منع التعيين مع استئناف معلق | `RecruitmentHireAppealGuard`, `RecruitmentHireClosureService` |
| J/K | الاختبارات والتوثيق | `Hcm028*`, `Hcm028AppealGovernanceMigrationTests`, `AcHcm28CommitteeAppealTests`, `hcm028-validate.ps1` |

الترحيلات (HR.Core): `HCM028_CommitteeDecisions`، `HCM028_RankOverride` (أعمدة وجداول جديدة فقط)، و`HCM028_AppealGovernance`
(أعمدة جديدة + خطوة بيانات Postgres **إدراج فقط**: `AppealNotes` بلا سجل استئناف ← استئناف `Decided` بنتيجة `LegacyReopen`).

## أوامر التحقق

```powershell
powershell -File scripts/hcm028-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm028"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm028"   # يحتاج Docker (Testcontainers)
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm28
```

الأدلة: `acceptance-evidence/AC-HCM-28/` (`steps.md`, `http-*.json`, `before.json`, `after.json`, `db-checks.json`, `result.json`).

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| AC-HCM-28a/c/d | `central-a` (أمين السر) → `br1-mgr` (الخطوتان 1–2) → `a-management-approver` (الخطوة 3) | الجلسة المبذورة على `REQ-2026-A-CMT1` |
| AC-HCM-28b | `central-a` | منشئ جلسة جديدة لا يعتمدها؛ عضو مكرر |
| AC-HCM-28e/f/g/i | `central-a` (رفض، تقديم، تعيين، استيفاء) + `a-compliance` (المراجع المعيّن) + `a-compliance-2` (غير معيّن) | طريق الاستئناف |
| AC-HCM-28h | `central-a` + `br1-mgr` + `a-management-approver` (العرض) + `a-compliance` | منع التعيين مع استئناف معلق |

## Manual — اللجنة (AC-HCM-28a..d)

1. `acc-central-a` → `RecruitmentRequisitions/Details` لـ `REQ-2026-A-CMT1`: بطاقة «لجنة الاختيار» تُظهر «يتطلب اعتماد لجنة الاختيار».
   سالب (c): محاولة ترتيب مرشح الآن ← «يتطلب الترتيب اعتماد لجنة الاختيار أولاً.».
2. `SelectionCommittees/Details` للجلسة المبذورة: الأعضاء (رئيس + 3). إضافة عضو بالمنتقي؛ إضافة الموظف نفسه مرة ثانية ← «الموظف عضو في هذه الجلسة مسبقاً.» (b).
3. مصفوفة الأصوات: «موافق» لكل عضو على كل مرشح. تسجيل **تعارض مصالح** لعضو مع المرشح الأول ← يُحذف صوته عليه فقط (d).
4. التوصيات: ترشيح بالرتب 1 و2، احتياط بالرتبة 3، مع التعليل.
5. **تقديم للاعتماد** ← لافتة القفل؛ إضافة عضو بعدها ← «تم تقديم الجلسة للاعتماد — العضوية مقفلة.» (a).
6. `acc-br1-mgr` يعتمد الخطوتين 1 و2، و`acc-a-management-approver` الخطوة 3. منشئ الجلسة لا يرى زر الاعتماد ويُرفض من الخادم
   «لا يجوز لعضو في اللجنة أو منشئها اعتماد الجلسة.» (b).
7. **تحميل المحضر** ← ملف PDF بالأعضاء والأصوات والتوصيات والاعتمادات.
8. في طلب الشاغر: رتبة مخالفة للمقترحة بلا سبب ← رفض؛ **«تطبيق ترتيب اللجنة»** ← الرتب 1..3 (c).

## Manual — الاستئناف (AC-HCM-28e..i)

1. `acc-central-a` → `RecruitmentApplications/Details` لمرشح «مُرتَّب» → «انتقالات الحالة» → «مرفوض» مع ملاحظة الرفض.
   بطاقة «الرفض والاستئناف» تُظهر نوع الرفض «قرار الاختيار» وتاريخه. لا توجد بطاقة «استئناف (مسار قديم)» (i).
2. **تقديم الاستئناف** (سبب 20 حرفاً على الأقل، نيابة عن المرشح) ← صفحة `RecruitmentAppeals/Details`: «لم يُعيَّن مراجع بعد».
3. **تعيين المراجع المستقل** من المنتقي: عضو لجنة الطلب ← «يجب أن يكون المراجع مستقلاً عن اللجنة والمقابلة والتقييم.» (e)؛
   موظف `acc-a-compliance` ← يبدأ «مسار مراجعة الاستئناف».
4. `acc-a-compliance` → القائمة: **استئنافات التوظيف** / **صندوق العمل** (لا يرى غير صفحات الاستئناف) → فتح الاستئناف →
   **قرار المراجع المستقل** مع تعليل 30 حرفاً:
   - «قبول — إعادة التقييم» ← الطلب «قيد الفرز» (e).
   - «رفض الاستئناف» ← يبقى «مرفوض» بنوع الرفض وتاريخه الأصليين (f).
   - «قبول — تصحيح الترتيب» ← رتبة مصحّحة (للمرشح المرتب سابقاً على طلب منشور).
   - «قبول دون أثر على الشاغر» ← يبقى «مرفوض» مع سجل تدقيق؛ وحده و«رفض الاستئناف» متاحان إن أُغلق الشاغر أو أُلغي.
   `acc-a-compliance-2` لا يرى الاستئناف، ويُرفض قراره من الخادم «غير مصرح: يجب أن تكون المراجع المستقل المعيّن.».
5. **طلب معلومات** (المراجع) ← «طلب المراجع معلومات إضافية…»؛ `acc-central-a` → **استيفاء وإعادة للمراجع** ← مسار جديد للمراجع نفسه.
6. **إعادة تعيين المراجع** أثناء التعليق تلغي المسار الجاري وتبدأ مساراً جديداً.
7. سالب (g): استئناف بعد 14 يوماً من تاريخ الرفض ← «انقضت مهلة الاستئناف على هذا الرفض.»؛ رفض بسبب إلغاء الشاغر ←
   «لا يقبل هذا النوع من الرفض الاستئناف.».

## Manual — منع التعيين (AC-HCM-28h)

1. على طلب شاغر فيه استئناف معلق: العرض للمرتب الأول مسموح (يتخطى المستأنِف في ترتيب الجدارة) ← اعتماد ← قبول.
2. `RecruitmentApplications/Details` للمرشح المقبول: تنبيه «يوجد استئناف معلق على هذا الشاغر — يجب البت فيه قبل التعيين.»
   ولا يظهر زر التعيين؛ إرسال نموذج التعيين يُرفض بالرسالة نفسها.
3. بعد البت في الاستئناف يعود التعيين متاحاً.

## ملاحظات

- لا تعديل على `WorkflowStageGuard`/`WorkflowEngine`؛ فحوص اللجنة والاستئناف في خدماتها، والاعتماد العام لمسار استئناف بلا
  نتيجة مخزّنة يُرفض في `WorkflowInstanceActionService` (خطاف الكيان).
- تقديم المرشح للاستئناف من `/careers` والإشعارات ← PLAN-30؛ أرشفة المحضر في File.Storage ← PLAN-29.
