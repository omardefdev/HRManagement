# تحقق مستقل — FUN_002_SEPARATION_GAP_STUDY

**المرجع:** لقطة `main` @ `f9eab1a` في `/home/claude/hrm`، قراءة ثابتة فقط (لا .NET SDK).
**النطاق:** كل فجوات P0 وP1 بالتفصيل، وأرقام أسطر P2/P3 فحصاً سريعاً.

## جدول الأحكام

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-SEP-01 | P0 | مؤكد | لا مرجع لـ `Step2CompleteStep` أو `OverrideChecklistStep` في أي `cshtml`/`js`، ولا API بديل، ولا مستهلك يضبط `IsCompleted` على خطوات الفصل. `Step2Confirm` والنشر يرفضان ما دامت خطوة ناقصة. صفحة التفاصيل بلا قائمة. |
| G-SEP-02 | P0 | مؤكد | `service-regimes.json` يُنسخ في `HR.Core.Infrastructure.csproj:16` فقط، ولا مرجع مشروع من HR.Contracts إليه، والـ Dockerfile ينسخ `/app/publish` فقط. لا `Integration__HrCoreBaseUrl` لـ `hr-contracts-web` في `docker-compose.services.yml` ولا `docker-compose.production.yml` ولا `hr-app-config` ولا `appsettings*.json`، فيقع العميل على `localhost:5010`. `law50` يسمح بثلاثة أنواع فقط. فحص النظام بعد القائمة. (عدم شحن الحزمة يبقى «يحتاج تحقق» كما وسمته الدراسة، والعطل يقع قبله في استدعاء HR.Core.) |
| G-SEP-03 | P0 | مؤكد | طرف الدليل يعطّل محلياً فقط، والمستهلكان اللاحقان يخرجان قبل `TryDisableUserAsync`. `DirectorySync.Enabled=false` ولا يفعّله أي إعداد إنتاج (compose وk8s يضبطان السر فقط). لا اعتراض أو خدمة خلفية تزامن `IsActive`، ولا فحص للحالة عند التحقق من الرمز. |
| G-SEP-04 | P0 | مؤكد جزئياً | `ApprovedBy`=المنشئ، و`HrContractsApprove` يشمل دور المنشئ، والمعاملة تُدفع إلى `Executing` بمستخدم `null`، والاستثناء بـ GUID من النموذج ✔. **خطآن:** (1) `hr-management-approver` لا يصل إلى `Step3Execute` في الإنتاج: البوابة تفرض `HrContractsApprove` على كل POST إلى `hr-contracts` خارج `HireWizard` (`GatewayPathAuthorizationMiddleware.cs:222-226`) والخدمة بلا منافذ (`docker-compose.production.yml:281`)؛ النقص دفاع في العمق فقط. (2) «صفر مرجع» لمحرك السياسات غير دقيق؛ HR.Contracts يستدعيه للإجازات (`LeaveRequestHandlers.cs:49`)، لا للفصل. الأولوية P0 تبقى لأن منشئاً واحداً ينهي الخدمة بلا اعتماد. |
| G-SEP-05 | P0 | مؤكد | `VacatePosition` لا يُستدعى إلا في تعويض التعيين والتنقل الداخلي. لا إجراء إخلاء في `FundedPositionsController` (إنشاء/تجميد/فك تجميد فقط). طرف HR.Core إقرار فارغ، ومستهلك الاكتمال لا يمس المنصب. |
| G-SEP-06 | P1 | مؤكد | لا مقارنة بـ `OwnerRole`؛ `CompletedBy=null` يمر لأن الفحص على `Guid.Empty` فقط. Admin.Logistics يضبط `ReturnedAt` ويحوّل الأصل إلى `InStorage` بلا دليل بينما الإرجاع اليدوي للمميز يشترطه (`AssetsController.cs:509`). المستهلك يختم دليل الرواتب ولا يضبط `IsCompleted`. |
| G-SEP-07 | P1 | مؤكد | تناقض `IsEffectiveDateReached` و`CanCancel` يجعل إلغاء ما بعد النشر غير قابل للوصول؛ الاختبار يفرض الحالة. التعويض في Admin.Logistics/IT/Auth/IDCard غير محصور. الرواتب تلغي المسودة فقط. في `Compensating` يقود الفشل إلى تدخل ثم `Resume` يرسل `Compensate=false`. لا مستهلك لـ `ICancelSeparationSaga` غير الـ saga، فالسجل والمعاملة لا يُحدَّثان. |
| G-SEP-08 | P1 | مؤكد | مدة الخدمة من `profile.EffectiveFrom`، والنوع لا يدخل الحساب، والصافي يُقصّ إلى صفر، والسلف تُحسم مرتين، والتشغيل يختار الملفات الفعّالة فقط، وغياب الملف يُقَر `payroll:settled`، والإجازات لسنة النفاذ فقط. |
| G-SEP-09 | P1 | مؤكد | `Create` يربط الكيان، ولا `CreatedBy`، وفحص الاعتماد يقارن بـ `ApprovedBy` الفارغ، و`MarkPaid` يغيّر الحالة فقط. ملاحظة: الإنشاء يتطلب `PayrollProcess` والاعتماد `PayrollApprove`، فـ«من يحسب يعتمد» يصح لمن يحمل الدورين (أو admin)؛ والجمع ممكن لأن `ValidateRoleAssignment` بلا مستدعٍ. |
| G-SEP-10 | P1 | مؤكد | تعديل الموظف يغيّر الحالة إلى نهائية بـ `HrCoreWrite`، والمستهلك الوحيد لـ `IEmployeeStatusChanged` هو Auth.Directory. `LimitedDuties` ينشر `IMedicalDecisionIssued` فيُنشأ فصل «طبي». إغلاق القضية ينشر حكماً لـ `Guid.Empty`. `IResignationSubmitted` بلا منتج. |
| G-SEP-11 | P1 | مؤكد | لا تحقق من الموظف ولا منع تكرار في الإنشاء اليدوي (المدخل الوارد وحده يمنع التكرار بالموظف والتاريخ والنوع). لا فهرس فريد على `SeparationRecord`. قائمة العقود آخر 500 لكل الموظفين. `Terminate` لا يضبط تاريخاً. لا `SeparationRecordId` في المعاملة، ولا رمز تزامن. |
| G-SEP-12 | P1 | مؤكد | البوابة الحقيقية تُسجَّل بمفتاح `Services:HRCore:BaseUrl`/`HrCore:BaseUrl` الموجود في `docker-compose.dev-contracts.yml:18` فقط. `HttpClient` عادي بلا معالج هوية (لا `ConfigureHttpClientDefaults`) إلى `HrCoreLookupS2S`، وأي فشل «allowing publish». |
| G-SEP-13 | P1 | مؤكد | `MandatoryParties` بلا HR.Health، ولا قسم `Sagas` في أي إعداد، وHR.Health لا يستهلك `ISeparationCompleted`. HR.Core يسجّل تحذيراً فقط. البند الفرعي عن `SchemaVersion` يبقى «يحتاج تحقق» كما وسمته الدراسة: يُحسم بنشر `ISeparationCompleted` من الـ saga الحقيقية في اختبار MassTransit وقراءة `SchemaVersion` عند المستهلك. |
| G-SEP-14 | P1 | مؤكد | الحالات النهائية لا تنتقل إلا إلى `Anonymized`. ربط موظف قائم لا يغيّر حالته. طرف الدليل في saga التعيين يشترط حساباً نشطاً. |
| G-SEP-19 | P1 | مؤكد | الأسطر المذكورة في `FUN_002_VERIFICATION.md:17` و`FUN_002_UAT_PACK.md:13` و`01:138` و`02:161` و`04:167` صحيحة. لا مجلد `AC-SEP` في `acceptance-evidence`. E2E يفحص الوصول فقط، وشرط الاستئناف `Consumed \|\| Published`، والمصفوفة تذكر `SeparationDecision` و`ITAccessRevoked`. |
| G-SEP-15 | P2 | أسطر صحيحة | `ServiceRegimeSeparationRules.cs:15, 33, 50`، `SeparationRecordViewModels.cs:22`، `insurance-jurisdiction-map.json:71`، `Program.cs:57`. |
| G-SEP-16 | P2 | أسطر صحيحة | `SeparationRecord.cs:33`. |
| G-SEP-17 | P2 | أسطر صحيحة | `DashboardController.cs:20`، Dev.Analytics `:52`، `HttpDsarSubjectSources.cs:158-160`، `ArchiveReferenceFactory.cs:30`، Records `SeparationCompletedConsumer.cs:23, 29`، `records-retention-matrix.json:14-21`، `OrgUnit.cs:27`. |
| G-SEP-18 | P3 | أسطر صحيحة | `SeparationWorkflowRules.cs:25`، `SeparationStateMachine.cs:49, 108-112`. |

## التعديلات على الدراسة

1. **G-SEP-04 (الدليل):** عبارة «بحث: صفر مرجع» صارت «لا يستدعي مسار الفصل… المحرك؛ الخدمة تستدعيه للإجازات فقط» مع المرجع.
2. **G-SEP-04 (الدليل):** بند `Step3Execute` يذكر الآن أن البوابة تفرض `HrContractsApprove` على كل POST إلى `hr-contracts` وأن الخدمة بلا منافذ في الإنتاج، فلا يصل `hr-management-approver` عملياً، والنقص دفاع في العمق.
3. **G-SEP-04 (الأثر):** أُضيفت ملاحظة التصحيح.
4. **الخلاصة 2(د):** استُبدل «ويستطيع `hr-management-approver` التنفيذ عبر المعالج» بـ «وتنفيذ المعالج بلا سياسة على مستوى الخدمة (البوابة وحدها تحصره بـ `HrContractsApprove`)».

لم تُغيَّر أي أولوية.

## ملاحظات خارج الفجوات (لا فجوات جديدة)

- ترتيب فحوص النشر الفعلي: تاريخ النفاذ ← الحجز القانوني ← القائمة ← نظام الخدمة (`SeparationRecordHandlers.cs:136-178`)، لا «القائمة ثم الحجز» كما في صف الخريطة 5. الفرق لا يغيّر أي فجوة.
- عند الوصول إلى `Cancelled` لا تنشر الـ saga `ISeparationSagaFinalized(Success=false)`، فلا يعرف HR.Contracts أن التعويض انتهى. هذا يقع ضمن G-SEP-07.
