# تحقق مستقل — HCM_006_EMPLOYEE_RELATIONS_GAP_STUDY

**النطاق:** كل فجوات P0 (G-ER-01..04) وأول ثلاث فجوات P1 (G-ER-05..07). قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-ER-01 | P0 | مؤكد | `Create` يضبط `AppealStatus=None` ثم يفحصه، فالحارس تحصيل حاصل. في المتحكمين 29 POST بلا `[Authorize]` (11 + 18)، والبوابة تفرض `ModuleCompliance` فقط. لا استدعاء لـ `IPolicyEnforceClient` ولا لمسار عمل في Compliance. المستهلك الوحيد لـ `IDisciplinaryActionTaken` في Payroll. وأسطر الوثائق الأربع صحيحة. |
| G-ER-02 | P0 | مؤكد | InboundEventHandlers.cs:610-614 يعطّل الملف، و`GetActiveProfileAsync` يرشّح على `IsActive`، فتُتخطى كتلة التسوية :206-244. لا منتج آلي لـ `SeparationType.Disciplinary` (يُذكر في البذرة فقط). |
| G-ER-03 | P0 | مؤكد جزئياً | المسار صحيح كاملاً: `Close` ينشر `Guid.Empty`، والمستهلك ينشئ انفصالاً `Judicial`، ولا مدقّق FluentValidation لـ `ProcessSeparationRequestCommand`. لكن عبارة «لا اختبار لـ `ILegalJudgmentIssued`» غير دقيقة، ففي Phase4Tests.cs:264-268 اختبار عقد لرقم النسخة. لا اختبار سلوكي، والأثر كما هو. |
| G-ER-04 | P0 | مؤكد | `ActionType` بأربع قيم، و`DisciplinaryAction` بلا مدة ولا مبلغ ولا مرجع. `PenaltyDays = 0` ثابت، فشرط الخصم لا يتحقق. الاختباران يستعملان 2 و3. |
| G-ER-05 | P1 | مؤكد جزئياً | الجوهر صحيح: `CanSeeReporter` يعيد `true` لكل دور `compliance` بعد أي إسناد، و`FiledBy` في التصدير الثلاثي، والنص في Dev.Analytics وعنوان الأرشيف، ولا تدقيق قراءة. لكن أسطر `ExportController` خاطئة: CSV في :37-39 لا :58-60، وExcel في :58-63 لا :79-84 (الملف 72 سطراً). |
| G-ER-06 | P1 | مؤكد | كل الأسطر صحيحة. بند «الأرشفة مرتين» الموسوم «يحتاج تحقق» تؤيده القراءة الثابتة: `ComplaintResolvedArchiveConsumer` يمنع التكرار بالعنوان، والعنوان يتضمن `Resolution`، فقرار ثانٍ بنص مختلف يُؤرشف بنداً ثانياً. |
| G-ER-07 | P1 | مؤكد | `IsRequired` لا يُقرأ خارج الإنشاء، و`StartInvestigation` بلا شرط إسناد، والإسناد بنص GUID، ولا `PerformedBy` في المتابعات. في Compliance مسار `NotificationHub` فقط (Program.cs:101)، ولا شيء يرسل عليه. |

## التعديلات على الدراسة
- G-ER-03: استُبدل بند «لا اختبار» بـ«لا اختبار سلوكي» مع ذكر Phase4Tests.cs:264-268، وأُضيفت ملاحظة التصحيح إلى سطر الأثر.
- G-ER-05: صُحّحت أسطر `ExportController.cs` (CSV :37-39، وExcel :58-63)، وأُضيفت ملاحظة التصحيح إلى سطر الأثر.

## ملاحظات جديدة (خارج الفجوات المكلَّف بها)
- `DisciplinaryActionsController.Create` يربط `model.Id` من نموذج العميل (ComplianceExtendedControllers.cs:181, 192-193). لذلك يستطيع العميل تحديد `ActionId`، وهو مفتاح علامة الرواتب `DisciplinaryAction-{ActionId}`. الخطر منخفض لأن `Add` بمعرّف مكرر يفشل بقيد المفتاح الأساسي.
- **سياق لـ G-ER-03 (لا يغيّر الحكم):** بإعدادات compose الحالية لا يظهر الأثر الموصوف بعد، لسببين:
  - `LegalCases.Create` يطلب رقماً من HR.Core، ونطاق `svc-compliance` هو `hr-core.lookup dsar.subject.read` (docker-compose.services.yml:366، sync-keycloak-dev-realm.ps1:535) بلا `hr-core.workflow` الذي يشترطه `generate`.
  - `ProcessSeparationRequestHandler` يرمي عند `ApplyReferenceAsync` لأن HR.Contracts بلا عنوان HR.Core في الإنتاج.

  الخلل في الشيفرة قائم، وسيظهر بمجرد إصلاح الإعداد، وهذا الإصلاح مقترح في COD_ORG.
