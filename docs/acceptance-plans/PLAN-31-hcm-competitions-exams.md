# PLAN-31 — المسابقات متعددة الشواغر والاختبارات (PLAN-HCM-I)

**Scope:** HCM-001 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-19  
**Status:** Implemented and accepted — AC-HCM-31a..g Pass (2026-10-29) — D-2 قرره المالك 2026-10-29؛ التنفيذ P0–P8 على الفرع
`feature/plan-31-competitions-exams`، التقرير: [`reports/plan31-final-report.md`](../../reports/plan31-final-report.md)  
**Priority:** P2 (يصبح P1 إن كان النظام القانوني المعتمد يشترط المسابقة للتعيين)  
**Depends on:** PLAN-26..30 منفذة، PLAN-25 مؤجلة

> ## تعديلات قبل التنفيذ (2026-10-29)
>
> طلب المالك تنفيذ PLAN-31 (القرار D-2، 2026-10-29). تحسم البنود التالية الفروق بين هذه الخطة وموجّه التنفيذ
> (`execution/PLAN-31-AGENT-PROMPT.md`)، ويُقدَّم الموجّه عند الاختلاف:
>
> 1. **D-2 والقيم القانونية:** الأساس القانوني وكل معامل قانوني **مُدخلات لكل مسابقة** تؤخذ من المرجع المعتمد: مدة
>    الاعتراض، صلاحية الاحتياط، درجة النجاح، أوزان المعايير، عتبة التصحيح المزدوج، قاعدة التسوية، ترتيب كسر التعادل،
>    والحد الأقصى للأسطر لكل متقدم. لا تُخترع أي قيمة قانونية. القيم الافتراضية موجودة فقط في
>    `appsettings.Development.json` وملف القبول، ولا افتراضيات في الإنتاج.
> 2. **طلب الشاغر يبقى منصباً ممولاً واحداً:** يبقى `VacancyCount` وقيد `CK_RecruitmentRequisitions_VacancyCount`، ولا
>    يُستبدل `ValidateVacancyCount`. **سطر المسابقة** يجمع عدة طلبات معتمدة، و`RequiredCount` = عدد الطلبات المربوطة (انحراف
>    عن «يُستبدل بقاعدة على مستوى السطر» أعلاه).
> 3. **طلب توظيف واحد لكل شخص في كل مسابقة** (`CompetitionId`) مع تفضيلات أسطر مرتبة، و`RequisitionId = null` حتى
>    التخصيص؛ التخصيص يضبط `RequisitionId` فيعمل مسار العرض ← التعيين ← العقد كما هو. فهرس فريد
>    `(TenantId, CompetitionId, NationalIdHash)`.
> 4. **الإشعارات لا تحمل درجة ولا رتبة ولا رقماً وطنياً** (حارس PII من PLAN-30): الرسالة الفردية تقول إن النتائج نُشرت
>    وتحيل إلى `/careers/my` حيث تظهر الدرجة والرتبة والنتيجة بعد رمز التحقق (انحراف عن «إشعار فردي بالدرجة والرتبة»).
> 5. **الاعتراضات تعيد استخدام `RecruitmentAppeal`** بنوع `ResultObjection` ونسخة النتائج، فيُطبَّق المراجع المستقل ومسار
>    المراجعة من PLAN-28، مع نتائج خاصة بالاعتراض (`ObjectionUpheld`/`ObjectionDismissed`).
> 6. **تصحيح مُغفَل:** دور مصحح مخصص (`hr-exam-marker`) يرى الأرقام الامتحانية فقط ولا يرى الهويات.
> 7. **العروض بالجملة** تُنشئ مسودات وتُرسل كل واحدة إلى مسار اعتماد العرض **القائم**؛ مسار
>    `RECRUITMENT-OFFER-BATCH-APPROVAL` اختياري ولا يُنفَّذ إلا بعد اجتياز كل ما سواه، وإلا يُوثَّق متابعةً.
> 8. **استدعاء الاحتياط** عند رفض العرض أو انتهائه أو إلغائه أو انسحاب متقدم مخصَّص، ضمن صلاحية احتياط السطر. الاستدعاء عند
>    عدم المباشرة مرتبط بمتابعة «إلغاء التعيين» المؤجلة في PLAN-29 (علامة `// PLAN-31 follow-up:`).
> 9. **PLAN-25 تبقى مؤجلة:** لا تغيير على بوابات سير العمل العامة ولا `WorkflowStageGuard`؛ تعريفات المسارات الجديدة تتبع
>    نمط البذر والمعالجات في PLAN-27/28.
>
> قرارات التنفيذ الإضافية (J1–J11: نافذة الاعتراض فترةٌ لا حالة، شكل معايير موحد للمسابقة، بديل بوابة اللجنة، خطوة اللجنة
> بأدوار متمايزة، صلاحية المصحح، …) مسجلة في `reports/plan31-progress.md`.

## Goal

دعم نمط التوظيف الحكومي الجماعي:

1. **مسابقة** واحدة لعدة شواغر ممولة (فئة/درجة/وحدات متعددة).
2. **اختبار** كتابي أو عملي بتصحيح مُغفَل الهوية.
3. **إعلان نتائج** مع **فترة اعتراض**.
4. **قائمة ناجحين وقائمة احتياط** بصلاحية محددة.
5. **عروض بالجملة** حسب الترتيب.

## الخلفية

- `RequisitionWorkflowRules.ValidateVacancyCount` (`:25-26`) يفرض `VacancyCount == 1`، وطلب الشاغر مربوط بمنصب ممول واحد.
- `Requariments.md` يحدد من مهام الشعبة «وضع آليات الانتقاء والاختبارات»، ويذكر أن فرع الخدمة والعقود يستقبل «بيانات المقبولين الجدد ونتائج الاختبارات».
- لا يوجد في الشيفرة كيان اختبار أو امتحان أو إعلان نتائج.

> **قبل البدء:** يُوثَّق الأساس القانوني في `docs/syr/legal-source-binding.md` (المرجع SYR-001 المعتمد للمسابقات)، وتُحدَّد قواعد الأوزان ومدة الاعتراض وصلاحية الاحتياط من المرجع لا من الافتراض.

---

## In scope

### 1. النموذج

| الكيان | الحقول الرئيسية | ملاحظات |
|--------|------------------|---------|
| `RecruitmentCompetition` | `Number`، `Title`، `LegalSourceId`، `Status`، `AnnouncementDate`، `ApplicationClosingDate`، `ObjectionWindowDays`، `ReserveValidityMonths`، `WorkflowInstanceId` | الحالات: Draft → PendingApproval → Approved → Announced → IntakeClosed → Examining → ResultsDraft → ResultsPublished → ObjectionWindow → Finalized / Cancelled |
| `CompetitionLine` | `CompetitionId`، `JobCatalogEntryId`، `GradeCode`، `RequiredCount`، `OrgUnitId?`، `RequisitionIds[]` | كل سطر يجمع طلبات شاغر (منصباً ممولاً لكل منها)؛ `RequiredCount` = عدد الطلبات المربوطة |
| `ExamSession` | `CompetitionId`، `Type` (Written/Practical/Oral)، `ScheduledAt`، `Venue`، `Capacity`، `DurationMinutes` | — |
| `ExamCandidate` | `ExamSessionId`، `ApplicationId`، `ExamNumber` (مُغفَل)، `SeatNumber`، `Attendance` | `ExamNumber` فريد ولا يكشف الهوية |
| `ExamScore` | `ExamCandidateId`، `MarkerUserId`، `Score`، `Round` (First/Second/Reconciled) | تصحيح مزدوج: إن تجاوز الفرق `Exam:ReconcileThreshold` يُطلب مصحح ثالث |
| `CompetitionResult` | `CompetitionLineId`، `ApplicationId`، `FinalScore`، `Rank`، `Outcome` (Successful/Reserve/Failed)، `PublishedAt` | لقطة ثابتة بعد النشر |
| `CompetitionObjection` | يعيد استخدام `RecruitmentAppeal` بنوع `ResultObjection` | تابع لـ PLAN-28 |

**التوافق:**

- طلب الشاغر المنفرد الحالي يبقى صالحاً (مسابقة ضمنية بسطر واحد).
- `RecruitmentApplication.CompetitionLineId` اختياري.
- `ValidateVacancyCount` يُستبدل بقاعدة على مستوى السطر.

### 2. المسار

1. **الإنشاء والاعتماد:** مسار `RECRUITMENT-COMPETITION-APPROVAL` (hr-core ← hr-management-approver)، مع شرط أن كل الطلبات المربوطة `Approved` وأن لكل سطر معايير تقييم تشمل مرحلة Exam.
2. **الإعلان:** إعلان عام واحد لكل مسابقة بقائمة الشواغر، والتقديم على سطر أو أكثر حسب `Competition:MaxLinesPerCandidate`، مع ترتيب تفضيل.
3. **التوزيع على القاعات:** آلي حسب السعة، مع توليد **بطاقة امتحان PDF** (`ExamAdmissionCardDocument`) تُرسل للمرشح عبر البوابة والإشعارات.
4. **الحضور ورصد الدرجات:** واجهة المصحح تعرض `ExamNumber` فقط، مع تصحيح مزدوج وتسوية.
5. **الدرجة النهائية:** حسب أوزان المعايير (Exam + Interview + Experience…)، تستخدم `RecruitmentScoringService` من PLAN-26.
6. **مسودة النتائج ثم اعتمادها:** مسار `RECRUITMENT-RESULTS-APPROVAL` (لجنة ← مدير HR).
7. **النشر:** قائمة عامة على `/careers/competitions/{id}/results` بالرقم الامتحاني والنتيجة فقط، وإشعار فردي بالدرجة والرتبة.
8. **فترة الاعتراض:** اعتراضات عبر البوابة؛ والقبول يعيد الحساب ويُصدر نسخة نتائج جديدة مع سجل.
9. **الإنهاء (Finalize):**
   - لكل سطر: أعلى `RequiredCount` ← `Ranked` على الطلبات بالترتيب، مع مراعاة التفضيل عند تعدد الأسطر (خوارزمية تخصيص بسيطة: الأعلى درجة يأخذ أعلى تفضيل متاح).
   - الباقون الناجحون ← `ReserveListed` حتى `ReserveValidityMonths`.
10. **العروض بالجملة:** إنشاء مسودات عروض للناجحين، ويبقى اعتماد كل عرض بالمسار المعتاد (أو مسار جماعي `RECRUITMENT-OFFER-BATCH-APPROVAL` لتقليل الحمل).
11. **استدعاء الاحتياط:** عند رفض أو عدم مباشرة ضمن صلاحية القائمة (يرتبط بـ PLAN-27/29).

### 3. الواجهة

- `Competitions/Index|Create|Details` (تبويبات: الأسطر، المتقدمون، الامتحانات، الدرجات، النتائج، الاعتراضات).
- شاشة المصحح.
- صفحات البوابة العامة: الإعلان، بطاقة الامتحان، النتائج.

---

## Out of scope

- بنك أسئلة أو اختبار إلكتروني عبر الإنترنت (يمكن لاحقاً كتكامل)
- مراقبة الامتحان عن بعد

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-31a | مسابقة بسطرين (3 + 2 شاغر) ← اعتماد ← إعلان | ربط طلب غير معتمد ← رفض |
| AC-HCM-31b | توزيع 120 متقدماً على قاعتين ← بطاقات امتحان | — |
| AC-HCM-31c | المصحح يرى الرقم الامتحاني فقط؛ فرق كبير ← مصحح ثالث | — |
| AC-HCM-31d | نشر النتائج ← قائمة عامة بلا أسماء + إشعار فردي | — |
| AC-HCM-31e | اعتراض مقبول ← نسخة نتائج جديدة | اعتراض بعد النافذة ← رفض |
| AC-HCM-31f | الإنهاء ← 5 `Ranked` موزعون بالتفضيل + احتياط | — |
| AC-HCM-31g | رفض عرض ← استدعاء أول احتياط من السطر نفسه | احتياط منتهي الصلاحية ← لا استدعاء |

## Verification

```powershell
powershell -File scripts/hcm031-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter "Hcm031"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "Hcm031"
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm31
# أداء: توزيع/حساب نتائج لـ 10,000 متقدم < 60 ثانية
pwsh perf/hcm031-competition-scale.ps1
```

## Definition of done

- مسابقة متعددة الشواغر تمر من الإعلان حتى العروض بالجملة.
- التصحيح مُغفَل ومزدوج، والنتائج منشورة مع فترة اعتراض.
- قائمة الاحتياط تُستدعى ضمن صلاحيتها.
- الطلب المنفرد القديم لم يتأثر (انحدار PLAN-21/22/26/27).
- AC-HCM-31a..g = Pass، واختبار الحجم ضمن الحد.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-31-hcm-competitions-exams.md` |
| Domain/Infra | كيانات المسابقة والامتحان، `CompetitionAllocationService`، `ExamSeatingService`، `CompetitionResultsService` |
| Documents | `ExamAdmissionCardDocument`، `CompetitionResultsDocument` |
| Workflows | `RECRUITMENT-COMPETITION-APPROVAL`، `RECRUITMENT-RESULTS-APPROVAL`، (اختياري) `RECRUITMENT-OFFER-BATCH-APPROVAL` |
| Migrations | `HCM031_Competitions`، `HCM031_Exams`، `HCM031_Results` |
| Tests | `Hcm031*`، `AcHcm31CompetitionTests`، `perf/hcm031-competition-scale.ps1` |
| Docs | `HCM_001_RECRUITMENT.md`، `docs/syr/legal-source-binding.md`، `HIRE_ONBOARDING_GUIDE.md` |

## حالة التنفيذ والانحرافات (2026-10-29)

| البند | الحالة |
|-------|--------|
| AC-HCM-31a..g | ✅ **Pass حيّاً** (2026-10-29) على مكدّس `hrm-acceptance` المبني من `main` بعد إعادة ضبط بيانات القبول (بصمة الحزمة تغيّرت بإضافة المصححين) — الأدلة في `acceptance-evidence/AC-HCM-31/`. كشف التشغيل ثلاثة عيوب أُصلحت باختبارات: دورة إدراج في بذر القبول، وسياسة البوابة لاعتماد النتائج (المدير كان يُرفض بـ 403)، ونطاق اعتماد العروض لشواغر البذر |
| اختبار الحجم | ✅ 10,000 متقدم، 4 أسطر، 20 جلسة: 5.9 ثانية على PostgreSQL 16 (الحد 60) |
| التكامل (PostgreSQL) | ✅ قيود التفرّد، التصحيح المتزامن، الإنهاء المتزامن، مسار التوزيع حتى التعيين |
| الحالة `ObjectionWindow` | لم تُنشأ كحالة: المسابقة تبقى `ResultsPublished` والمدة تُحسب من أول نشر (J1) |
| لجنة الاختيار (PLAN-28) | تحل محلها خطوة «لجنة المسابقة» في اعتماد النتائج للطلبات الملحقة بمسابقة (J4/J5) |
| `RECRUITMENT-OFFER-BATCH-APPROVAL` | لم يُنفذ (اختياري، القرار 7) — كل عرض يمر باعتماده القائم |
| عدم المباشرة بعد التعيين | لا استدعاء تلقائياً من الاحتياط؛ علامة متابعة `// PLAN-31 follow-up:` (القرار 8) |
| إشعار HR بنفاد الاحتياط | في تبويب التوزيع وسجل التدقيق (لا قناة دفع لـ HR) |
| وصول المصحح | إدخالات خاصة بالدور على نمط PLAN-28 (لا تغيير عام في البوابات؛ PLAN-25 مؤجلة) |

قائمة القرارات J1–J39 في [`reports/plan31-progress.md`](../../reports/plan31-progress.md).

## Effort estimate

12–15 يوم عمل: النموذج والترحيل (3)، المسار والاعتمادات (2)، الامتحانات والتصحيح (3)، النتائج والاعتراض (2)، التخصيص والعروض بالجملة (2)، البوابة والوثائق (1.5)، القبول والأداء (1.5).
