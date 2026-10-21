# PLAN-26-EXEC — معايير التقييم ودورة المقابلة وسلامة طلب الشاغر (HCM-D)

تنفيذ [PLAN-26](../PLAN-26-hcm-assessment-interview-requisition.md) لإثبات **AC-HCM-26a..i**.

## المتطلبات

- Docker stack: `pwsh scripts/acceptance-up.ps1` (صورة `hr-core-web` مبنية من الشيفرة الحالية — تُطبَّق ترحيلات `HCM026_*` عند الإقلاع)
- قبل ترحيل قاعدة قائمة: `pwsh scripts/hcm026-find-duplicate-requisitions.ps1` يجب أن يُرجع «No duplicates» (الترحيل `HCM026_RequisitionIntegrity` يرفض إنشاء الفهرس عند وجود تكرار)
- Keycloak personas: `central-a`, `br1-mgr`, `a-management-approver`
- `NationalScope:IsLabScale=true` (اعتماد طلب الشاغر بخطوتين — PLAN-21)
- إعدادات `Recruitment` (القيم الافتراضية): `RequireCriteriaBeforePublish=true`، `MinAssessors=1`، `MaxInterviewReschedules=1`، `MinPostingDays=7`، `Jobs:IntakeCloseEnabled=true`، `Jobs:IntakeCloseIntervalMinutes=60`

## مراحل التنفيذ

| # | المرحلة | الملفات الرئيسية |
|---|---------|------------------|
| A | معايير التقييم المُصدَّرة | `AssessmentCriteriaRules`, `RecruitmentAssessmentCriteriaService`, `RecruitmentAssessmentCriteriaController`, `_AssessmentCriteriaPanel`, `recruitment-criteria.js` |
| B | الدرجات المنظمة متعددة المقيّمين | `RecruitmentAssessmentScore`, `AssessmentScoringRules`, `RecruitmentScoringService`, `_AssessmentScoringPanel` |
| C | دورة المقابلة | `RecruitmentInterviewService`, `RecruitmentInterviewPanelMember`, `_InterviewsPanel` |
| D | سلامة طلب الشاغر | `RequisitionWorkflowRules.BlockingStatuses`, `RecruitmentRequisitionCascade`, `EstablishmentRecruitmentCascadeRules`, `EstablishmentChangeApplier` |
| E | فترة التقديم والإغلاق الآلي | `ApplicationClosingDate`, `ExtendClosingDate`, `RecruitmentIntakeCloser`, `RecruitmentIntakeCloseJob` |
| F | البذور | `AcceptanceDataSeeder`, `DemoDataSeeder`, `RecruitmentSeedCriteria` |
| G/H | الاختبارات والتوثيق | `Hcm026*`, `AcHcm26AssessmentInterviewTests`, `hcm026-validate.ps1` |

الترحيلات: `HCM026_CriteriaGovernance`، `HCM026_AssessmentScores`، `HCM026_InterviewLifecycle`، `HCM026_RequisitionIntegrity`.

## أوامر التحقق

```powershell
powershell -File scripts/hcm026-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "FullyQualifiedName~Hcm026"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FullyQualifiedName~Hcm026"   # يحتاج Docker (Testcontainers)
pwsh scripts/hcm026-find-duplicate-requisitions.ps1
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm26
```

## Personas

| AC | Persona | الغرض |
|----|---------|--------|
| كل السيناريوهات | `central-a` → `br1-mgr` → `a-management-approver` | إنشاء طلب الشاغر → اعتماد خطوة 1 → اعتماد خطوة 2 |
| AC-HCM-26a/b/d | `central-a` | معايير التقييم، بوابة النشر، تغيير إصدار مُقفل |
| AC-HCM-26c | `central-a` + `br1-mgr` | مقيّمان مستقلان على الطلب نفسه |
| AC-HCM-26e | `central-a` | جدولة/إكمال/إعادة جدولة/إلغاء المقابلة |
| AC-HCM-26f/g | `central-a` (يملك `HrCoreApprove`) | منع الطلب المكرر، إلغاء طلب منشور |
| AC-HCM-26h | `central-a` + معتمد `hr-core` مركزي ثانٍ + `a-management-approver` | تجميد المنصب عبر مسار `ESTABLISHMENT-CHANGE-APPROVAL` |
| AC-HCM-26i | `central-a` | انتهاء فترة التقديم والإغلاق الآلي |

> **ملاحظة AC-HCM-26h:** طلبات تغيير الملاك مركزية فقط، والخطوة 1 تتطلب `hr-core` غير المنشئ (SoD). كتالوج الشخصيات الحالي لا يحوي معتمداً مركزياً ثانياً، لذلك يُتخطّى الاختبار الحي بـ `Skip.If`، والتتابع مغطّى على PostgreSQL في `Hcm026EstablishmentCascadeIntegrationTests`.

## Manual — معايير التقييم والنشر (AC-HCM-26a/b/d)

1. `acc-central-a` → `RecruitmentRequisitions/Create`: اختر الفرع ثم منصباً شاغراً، واحفظ المسودة (تاريخ إغلاق التقديم اختياري هنا).
2. **تقديم للاعتماد** → `acc-br1-mgr` **اعتماد** → `acc-a-management-approver` **اعتماد** → الحالة `Approved`.
3. في `RecruitmentRequisitions/Details` يظهر تنبيه «لا توجد معايير تقييم نشطة لهذا الطلب — يجب اعتماد معايير التقييم قبل النشر».
4. سالب: **نشر الإعلان العام** دون معايير → رفض «يجب اعتماد معايير التقييم قبل النشر.»
5. بطاقة «معايير التقييم» → «إصدار جديد»: أدخل بنوداً مجموع أوزانها 90 → زر الاعتماد معطّل والمجموع أحمر؛ وإن أُرسل يُرفض «مجموع الأوزان يجب أن يساوي 100».
6. موجب: ثلاثة بنود (المقابلة 40 / الاختبار 30 / الخبرة 30، المرحلة لكل بند) → **اعتماد الإصدار الجديد** → v1 «نشط».
7. **نشر الإعلان العام** مع **تاريخ إغلاق التقديم** (بعد 7 أيام على الأقل) → `Published`؛ الإعلان والشاغر الداخلي يأخذان التاريخ نفسه.
8. سالب (AC-HCM-26d): بعد أول درجة يصبح الإصدار «مُقفل»؛ إنشاء إصدار جديد يتطلب صلاحية الاعتماد وسبباً مكتوباً (10 أحرف على الأقل)، وإلا يُرفض «تغيير معايير بدأ التقييم عليها يتطلب سبباً مكتوباً».

## Manual — المقابلة والتقييم والترتيب (AC-HCM-26c/e)

1. `RecruitmentApplications/Index` → «طلب توظيف جديد» على الطلب المنشور.
2. `Details` → انتقالات الحالة: «قيد الفرز» → إقرار تعارض المصالح → «في القائمة المختصرة». (لا يظهر «مُقيَّم» ضمن الانتقالات اليدوية.)
3. بطاقة «المقابلات» → «جدولة مقابلة»: موعد في الماضي → رفض «موعد المقابلة يجب أن يكون في المستقبل.»
4. جدولة صحيحة: موعد مستقبلي، النمط، المكان/الرابط، لجنة المقابلة (أول عضو رئيس) → الحالة «مقابلة مجدولة».
5. «ورقة التقييم»: إدخال درجة بند «المقابلة» قبل إكمال المقابلة → رفض «بنود مرحلة المقابلة لا تُقيَّم إلا بعد إكمال المقابلة بنتيجة «حضر».»
6. بعد موعد المقابلة: **إكمال** بنتيجة «حضر» (أو «لم يحضر» — لا تتغير حالة الطلب).
7. المقيّم 1 (`acc-central-a`): 80 / 70 / 90 + إقرار «أقر بعدم وجود تعارض مصالح مع هذا المرشح» → مجموعه 80.00 → الحالة آلياً «مُقيَّم».
8. المقيّم 2 (`acc-br1-mgr`): 60 / 50 / 70 → مجموعه 60.00 → الدرجة النهائية 70.00 (متوسط المقيّمين المكتملين).
9. **تعيين الترتيب** 1 → «مُرتَّب». سالب: جدولة مقابلة لطلب مُرتَّب → رفض والحالة باقية «مُرتَّب».
10. إعادة الجدولة (مرة واحدة افتراضياً) تتطلب سبباً (5 أحرف)، والإلغاء يتطلب سبباً ولا يغيّر حالة الطلب.

## Manual — سلامة طلب الشاغر (AC-HCM-26f/g/h)

1. سالب (f): إنشاء طلب شاغر ثانٍ على منصب عليه طلب `Draft/PendingApproval/Approved/Published` → «يوجد طلب شاغر نشط مسبقاً على هذا المنصب الممول.» (ويرفضه أيضاً الفهرس `IX_RecruitmentRequisitions_Tenant_Position_Blocking`).
2. موجب (g): `RecruitmentRequisitions/Details` لطلب منشور → بطاقة «إلغاء طلب الشاغر» → سبب (10 أحرف) → يُخفى الإعلان، يُغلق الشاغر الداخلي، تُسحب العروض غير المقبولة، تُرفض الطلبات المفتوحة («إلغاء الشاغر: …») وتُلغى مقابلاتها المجدولة.
3. سالب (g): إلغاء طلب عليه مرشح «عرض مقبول» أو «قيد استكمال التعيين» → رفض.
4. الإغلاق: **إغلاق** يُرفض ما دام هناك عرض قيد الإجراء؛ وإلا تُرفض الطلبات المفتوحة («إغلاق الشاغر»).
5. (h) `FundedPositions/Details` → «تجميد» مع سبب → `EstablishmentChanges` تقديم واعتماد بخطوتين: بلا عرض صادر يُلغى طلب الشاغر آلياً («تجميد المنصب: …»)؛ مع عرض `Issued` أو مقبول غير محوّل يفشل التطبيق «لا يمكن تجميد منصب عليه عرض توظيف صادر أو مقبول — ألغِ العرض أولاً.»

## Manual — فترة التقديم (AC-HCM-26i)

1. بطاقة «تمديد تاريخ إغلاق التقديم»: تاريخ أسبق أو بلا سبب → رفض؛ تاريخ لاحق + سبب → يُحدَّث الطلب والإعلان والشاغر الداخلي.
2. بعد انقضاء التاريخ تُغلق مهمة `RecruitmentIntakeCloseJob` (كل 60 دقيقة) التقديم: الإعلان غير منشور، الطلب يبقى «منشور» للاختيار، شارة «التقديم مغلق» في القوائم وفي `/careers/{id}`.
3. التقديم بعد الإغلاق (HR أو `/careers`) يُرفض «التقديم على هذا الشاغر مغلق.»
4. التمديد بعد الإغلاق الآلي يعيد فتح التقديم (إن بقي الطلب منشوراً والمنصب شاغراً).

## الأدلة

| AC | مجلد | Assertions |
|----|------|------------|
| AC-HCM-26 | `acceptance-evidence/AC-HCM-26/` | `steps-26a-e.md`, `steps-26f-g.md`, `steps-26g-neg.md`, `steps-26i.md`, `http/*.json`, `db-checks.json` (الدرجات وحالة الإصدار)، `timing.json` (`intakeCloseSeconds`) |

> رسائل TempData (التنبيهات بعد إعادة التوجيه) لا تصل عبر البوابة لأن `GatewayUpstreamCookieForwarding` يمرّر كوكيز antiforgery فقط؛ لذلك تُثبت الحالات السالبة بفحص قاعدة البيانات، وتُسجَّل الرسائل المتوقعة في `db-checks.json`.

## Regression

- `Hcm021` / `Hcm022` / `Wf023` / `Est0*` / `RecruitmentHireClosure` / `PublicCareers` يجب أن تبقى PASS.
- لا تغيير على `WorkflowStageGuard` / `WorkflowEngine` (PLAN-25 مؤجلة).
