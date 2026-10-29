# PLAN-32 — تقارير ومؤشرات التوظيف (PLAN-HCM-J)

**Scope:** HCM-001 + ANA-001 + STR-007 — [دراسة الفجوات](../HCM_001_GAP_STUDY.md): G-21  
**Status:** In progress — التنفيذ P0–P8 على الفرع `feature/plan-32-recruitment-analytics`  
**Priority:** P2  
**Depends on:** PLAN-26..31 منفذة، PLAN-25 مؤجلة

> ## تعديلات قبل التنفيذ (2026-10-29)
>
> تحسم البنود التالية الفروق بين هذه الخطة وموجّه التنفيذ (`execution/PLAN-32-AGENT-PROMPT.md`)، ويُقدَّم الموجّه عند
> الاختلاف. التعريفات الدقيقة لكل مؤشر في `docs/STR_007_KPI_DEFINITIONS.md` و`docs/str-value/kpi-matrix.json`.
>
> 1. **الأزمنة من `RecruitmentApplicationEvent`** (PLAN-30، تُسجَّل منذ 2026-10-23). الصفوف الأقدم تستعمل **قرائن موثقة**
>    (`ScreenedAt`، `RankedAt`، تواريخ العروض، `HiredEmployeeId`)، ويعرض كل مؤشر زمني **التغطية** (نسبة الصفوف ذات سجل
>    أحداث). **لا تعبئة لاحقة لأحداث مصطنعة.**
> 2. **زمن الملء** يبدأ من `ApprovedDate` لطلب الشاغر (وعند غيابه `RequestedDate` ويُعدّ «بديلاً» في التغطية)، اتساقاً مع
>    مؤشر STR-007 القائم `hire_cycle_time` («الشاغر المعتمد»). لا يُعدَّل `hire_cycle_time` (تعريفه مجمّد — مكافحة
>    التلاعب)، وتُضاف مؤشرات جديدة. هذا انحراف مقصود عن `RequestedDate` الوارد في جدول المؤشرات أدناه.
> 3. **مزيج القنوات** من `RecruitmentApplication.IntakeChannel` (PLAN-30) لا من أعمدة المعرّفات المصدرية.
> 4. **أسباب رفض العروض** لها فئة: عمود جديد `OfferLetter.DeclineReasonCategory` إلزامي للرفض الجديد في مساري الموارد
>    البشرية والبوابة، مع بقاء النص الحر. الصفوف القديمة تظهر «غير مصنف». العروض المنتهية نتيجة مستقلة لا سبب.
> 5. **مهلة الاعتماد (SLA):** `TimeoutHours` للخطوة إن وُجد، وإلا `Recruitment:Analytics:ApprovalSlaHours:{WorkflowCode}`،
>    وإلا `Recruitment:Analytics:DefaultApprovalSlaHours` (48). لا يضبط أي تعريف مبذور `TimeoutHours` اليوم، فهذه **قيمة
>    سياسة تنظيمية** مؤقتة وموثقة وليست قيمة قانونية.
> 6. **النطاق** عبر `OrgScopeService`. وحدة طلب الشاغر = `OrgUnitId` لمنصبه الممول. المستخدم المقيّد (مدير الفرع) يرى
>    طلبات وحدته وطلبات التوظيف عليها فقط، وفلتر وحدة خارج النطاق يعيد «لا بيانات». مجاميع المسابقات **لغير المقيدين
>    فقط**. طلب المسابقة المخصَّص يُحسب على وحدة طلب الشاغر المخصَّص له.
> 7. **الخصوصية:** لا رقم وطني (ولا مقنّعاً) في أي تقرير أو قائمة تفصيلية أو تصدير. أسماء المرشحين **لـ `HrCoreWrite` فقط**،
>    وغيرهم يرى مرجع الطلب. حجب الخلايا الصغيرة (< 5 ← «<5») لكل ما يُنشر خارج HR.Core (البند 9) ولكل تفصيل بسمة شخصية.
>    **مؤشرات العدالة مؤجلة**: لا قرار GOV-001، وجنس المرشح غير مجمّع.
> 8. **أعداد دقيقة** داخل المستأجر (ANA-001): لا عيّنات ولا تقريب. لا تُبنى لقطة يومية إلا إذا فشلت ميزانية الأداء في P5.
> 9. **النشر إلى Dev.Analytics وتسجيل طبقة الدلالة** ضمن النطاق (P6) وبشكل **إضافي**: البيانات التجريبية و`Ac19` بلا تغيير.
> 10. **PLAN-25 تبقى مؤجلة**: لا تغيير لصلاحيات أو نطاق شاشات التوظيف القائمة. `RecruitmentApplicationsController`
>     و`RecruitmentRequisitionsController` لا يطبّقان نطاق الوحدة اليوم، بخلاف `RecruitmentAppealsController`
>     و`ReportsController`؛ تُسجَّل ملاحظة متابعة في التقرير النهائي.

## Goal

إعطاء الإدارة رؤية تشغيلية وقياسية لمسار التوظيف: قمع لكل طلب شاغر، وأزمنة الإنجاز، ونسب القبول والرفض والاستئناف، والاختناقات في الاعتمادات، مع تصدير يحترم نطاق الوحدة وتقليل البيانات الشخصية.

## الوضع الحالي

- لا توجد شاشة تقارير للتوظيف.
- `Dev.Analytics` يحوي بيانات تجريبية فقط (`DemoAnalyticsSeeder.cs`).
- مكتبتا ClosedXML وQuestPdf متوفرتان، وطبقة الدلالة PROD-006 موجودة.

---

## In scope

### 1. المؤشرات (تُعرَّف في `docs/STR_007_KPI_DEFINITIONS.md`)

| المؤشر | التعريف | المصدر |
|--------|---------|--------|
| قمع الطلب | عدد الطلبات في كل حالة لكل طلب شاغر/وحدة/فترة | `RecruitmentApplications` |
| زمن الملء (Time-to-Fill) | `RequestedDate` ← `Hired` (أيام، وسيط و P90) | Requisition + Application |
| زمن التعيين (Time-to-Hire) | `AppliedAt` ← `Hired` | Application |
| زمن الاعتماد | لكل مسار: عمر كل خطوة (`StepHistoryJson`) ومتوسطه، وتجاوز SLA | WorkflowInstances |
| نسبة قبول العروض | Accepted / (Accepted + Declined + Expired) | OfferLetters |
| أسباب رفض العروض | توزيع `DeclineReason` | PLAN-27 |
| مزيج القنوات | عام / داخلي / HR | `SourceCitizenApplicationId`، `SourceInternalApplicationId` |
| معدل الاستئناف ونتائجه | استئنافات / مرفوضين؛ Upheld/Dismissed | RecruitmentAppeals (PLAN-28) |
| تجاوزات الجدارة | عدد العروض بـ `IsMeritOverride` | PLAN-27 |
| الشواغر المفتوحة | مناصب `Vacant` بلا طلب نشط، وطلبات منشورة منذ > N يوماً | EST-001 + Requisitions |
| مؤشرات العدالة (اختياري) | توزيع نسب الانتقال حسب الجنس/الفئة العمرية **مجمّعاً فقط** (k ≥ 10) | بقرار الخصوصية GOV-001 |

### 2. التنفيذ

- **`RecruitmentMetricsService`** (HR.Core.Infrastructure): استعلامات قراءة فقط (`AsNoTracking`) بفلاتر الفترة والوحدة (عبر `EstablishmentScopeService`، فيرى كل مستخدم نطاقه فقط) وطلب الشاغر.
- **أداء:** فهارس داعمة إن لزم (`RecruitmentApplications(TenantId, RequisitionId, Status)` موجود جزئياً). للأحجام الكبيرة: لقطة يومية `RecruitmentDailySnapshot` تولدها مهمة ليلية.
- **الواجهة** `RecruitmentReports/Index` (`HrCoreReadOnly`): بطاقات مؤشرات + رسم قمع + جدول طلبات الشاغر بأعمارها + جدول الاعتمادات المتأخرة، مع روابط للتفاصيل. تُتبع `docs/ANA_001_ANALYTICS.md` وأسلوب الرسوم المعتمد في المشروع.
- **التصدير:** Excel (ClosedXML) وPDF (QuestPdf) عبر آلية `EXPORT_JOBS.md` الموجودة، بلا رقم وطني، وبأسماء المرشحين فقط لمن يملك `HrCoreWrite`.
- **التكامل التحليلي (اختياري):** تسجيل المقاييس في طبقة الدلالة PROD-006، ونشر حدث `IRecruitmentRequisitionClosed` (أرقام مجمعة) لـ Dev.Analytics بدل البيانات التجريبية.

---

## Out of scope

- تحليلات تنبؤية
- لوحات خارجية (Grafana/BI) — يمكن لاحقاً عبر التصدير

## Acceptance scenarios

| ID | Positive | Negative |
|----|----------|----------|
| AC-HCM-32a | بيانات بذرة معروفة ← قيم القمع وزمن الملء مطابقة للحساب اليدوي | — |
| AC-HCM-32b | مدير فرع يرى طلبات وحدته فقط | طلب تقرير وحدة خارج النطاق ← لا بيانات |
| AC-HCM-32c | تصدير Excel ← بلا رقم وطني | مستخدم قراءة فقط ← تصدير بلا أسماء |
| AC-HCM-32d | اعتماد متأخر عن SLA يظهر في جدول المتأخرات | — |

## Verification

```powershell
powershell -File scripts/hcm032-validate.ps1
dotnet test src/Tests/HRManagement.UnitTests --filter Hcm032
$env:RUN_ACCEPTANCE = "true"; dotnet test src/Tests/HRManagement.AcceptanceTests --filter AcHcm32
```

## Definition of done

- المؤشرات معرّفة رسمياً في `STR_007_KPI_DEFINITIONS.md` ومطابقة لحساب مرجعي في اختبار.
- التقرير يحترم نطاق الوحدة وتقليل البيانات.
- AC-HCM-32a..d = Pass.

## Deliverables

| Artifact | المسار |
|----------|--------|
| Plan | `docs/acceptance-plans/PLAN-32-hcm-recruitment-analytics.md` |
| Infra/Web | `RecruitmentMetricsService.cs`، `RecruitmentReportsController.cs` + Views، (اختياري) `RecruitmentDailySnapshotJob` |
| Tests | `Hcm032RecruitmentMetricsTests`، `AcHcm32RecruitmentReportsTests` |
| Docs | `STR_007_KPI_DEFINITIONS.md`، `ANA_001_ANALYTICS.md`، `QUERY_EXPORT_INVENTORY.md` |

## Effort estimate

4–5 أيام عمل: الخدمة والمؤشرات (2)، الواجهة (1)، التصدير (0.5)، الاختبارات (1)، اختياري: الدلالة/اللقطة (+1).
