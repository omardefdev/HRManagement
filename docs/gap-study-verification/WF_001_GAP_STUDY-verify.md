# تحقق مستقل: WF_001_GAP_STUDY.md

**المدقق:** وكيل تحقق مستقل · **الشيفرة:** `/home/claude/hrm` @ `f9eab1a` · **المنهج:** فتح كل مرجع ملف/سطر لفجوات P0/P1، والبحث عن أدلة مضادة (سياسات عامة، مرشحات، إعدادات compose/k8s، اختبارات)، وفحص أرقام الأسطر فقط لفجوات P2/P3.

## جدول الأحكام

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-WF-01 | P0 | مؤكد | كل المراجع صحيحة. `SeparationWizardController` بلا `[Authorize]` خاص، ويكفيه مرشح الوحدة `ModuleHrContracts` (يشمل `hr-management-approver` أيضاً). `PublishSeparationHandler` يفحص أيضاً حجز دورة الحياة ونظام الخدمة، وكلاهما ليس اعتماداً. وإجراء `Dismissal` من Compliance يعطّل ملف الأجر فوراً في Payroll (`InboundEventHandlers.cs:610-614`). في الإنتاج يفشل إنشاء الفصل أصلاً قبل ذلك عند توليد الرقم (G-COD-01). |
| G-WF-02 | P0 | مؤكد | لا إجراء في AdminLogistics يلمس `PendingApproval` (الإرجاع يشترط `Active`). الرابط يمرّر `workflowInstanceId` إلى `Review(Guid id)` فيعيد 404. `admin-logistics` خارج `HrCoreBranchOperation` و`ModuleHrCore`. البذرتان القديمتان بلا مستدعٍ. |
| G-WF-03 | P0 | مؤكد | `AddPostgresOutbox` مع `UseBusOutbox` في HR.Core وHRMovements. لا مرشح ولا `[Idempotent]` يستدعي `SaveChanges` بعد الإجراء. الأسطر 117/119-120/164-166 و1032/1034 و1189/1191 صحيحة. `OutboxAtomicityIntegrationTests` يثبت أن الفريق يعرف قاعدة «النشر ثم الحفظ». التحقق الحي المقترح في الدراسة يحسم. ملاحظة جانبية: عبارة «لكل خدمة» غير دقيقة (AdminLogistics وRecordsArchive بلا outbox)، ولا تؤثر في النتيجة. |
| G-WF-04 | P0 | مؤكد جزئياً | الصلاحية والنشر بلا تحقق مؤكدان. لكن سيناريو طلب الشاغر والعرض خاطئ: `WorkflowStarter` يقدّم `PolicyWorkflowBinding` المثبَّت على v1 (`WorkflowStarter.cs:93-113`)، والبذرة تربط `RECRUITMENT-REQUISITION/OFFER/OFFER-OVERRIDE` بمعرّف v1 (`PolicyEngineSeeder.cs:172-208`)، ولا واجهة تعدّل الربط أو تسحب نشر v1. السيناريو صحيح للملاك واللجنة والاستئناف والمسابقة والنتائج. الأولوية P0 باقية. |
| G-WF-05 | P1 | مؤكد | كل المراجع صحيحة (`docker-compose.production.yml:439-441`، `ServiceIdentityClient.cs:141-145`). ملاحظتان: (1) الاستغلال غير ممكن اليوم طرفاً لطرف لأن الحدث لا يغادر الـ Outbox (G-WF-03). (2) نسخة يبدؤها المستخدم نفسه عبر `Start` تحتاج معتمداً ثانياً للخطوة 2 (قاعدة المنشئ)، إلا إن بُدئت عبر `policies/enforce` بـ `ActorUserId` مزوّر (G-POL-08). |
| G-WF-06 | P1 | مؤكد | في المسار الحقيقي `InitiatedBy` حساب خدمة، والمحرك يمنع الفاعل نفسه في الخطوة نفسها فقط، فمستخدم `hr-core` واحد يعتمد الخطوتين. AC-11 يُظهر مدير فرع يعتمد خطوة `hr-core`. |
| G-WF-07 | P1 | مؤكد | لا تحقق من أن المعتمد الثاني يحمل دوراً أو أنه موجود. `SoDForgedApproverTests` يغطي الإجازة والسلفة والنقل وسجل التوظيف فقط، لا Payroll ولا استثناء قائمة الفصل. |
| G-WF-08 | P1 | مؤكد | `ValidateAction` يمنع تكرار آخر معتمد فقط، ولا مرحلة ثانية. ملاحظة: واجهة HR تحدّ السلفة بـ 100,000، فعتبة 5,000,000 لا تُبلغ منها، لكن قناة ESS تتجاوز الحد (انظر الملاحظات الجديدة). |
| G-WF-09 | P1 | مؤكد جزئياً | الإيفاد يسجّل منشئه (`MissionFile : AuditableEntity`، و`AuditSaveChangesInterceptor` يختمه)، فعبارة «لا منشئ مسجَّل» خاطئة، والغائب هو المقارنة وسياسة الاعتماد. بقية البنود مؤكدة (DSAR، تسوية نهاية الخدمة، خطط التوزيع، `Advance`، الإجازة). |
| G-WF-10 | P1 | مؤكد | المراجع صحيحة. ملاحظة: `Reject` في الإجازة والسلفة بلا سياسة الاعتماد يوسّع الرفض عملياً إلى `hr-management-approver` فقط (مرشح الوحدة)، فأثره العملي محدود. لا رفض ولا إلغاء للترفيع والندب. |
| G-WF-11 | P1 | مؤكد | لا قارئ لـ `ApprovalDelegations` ولا `WorkflowDelegations` خارج متحكم التفويض. |
| G-WF-12 | P1 | مؤكد جزئياً | عبارة «لا xmin في HR.Core كله» خاطئة: `Employee` و`ApprovalRequest` يحملانه (`HRCoreDbContext.cs:152، 579`). غياب الرمز على `WorkflowInstance`، والفهرس غير الفريد، وعدم ذرّية النسخة والأثر مؤكدة. |
| G-WF-13 | P1 | مؤكد | البطاقات «Done»، و`CreateViaEngineOrLegacy` لا يستدعيه إلا اختبار، وAC-33 يقبل 403 (السطر 31)، وAC-12 «Blocked». |
| G-WF-14 | P2 | أسطر صحيحة | `ApprovalSlaRules.cs:7` تعليق التوثيق للدالة، مقبول. |
| G-WF-15 | P2 | أسطر صحيحة | — |
| G-WF-16 | P2 | أسطر صحيحة | — |
| G-WF-17 | P2 | أسطر صحيحة | — |
| G-WF-18 | P2 | أسطر صُحّحت | أضيف نطاق `Edit` (POST) `155-211` إلى المرجع. |
| G-WF-19 | P3 | أسطر صحيحة | — |

**الحصيلة (P0/P1):** مؤكد 10 · مؤكد جزئياً 3 (G-WF-04، G-WF-09، G-WF-12) · يحتاج تشغيلاً 0 · خطأ 0.

## التعديلات على الدراسة

1. **G-WF-04:** استُبدل السيناريو بآخر على `ESTABLISHMENT-CHANGE-APPROVAL`. وأضيف بند «استثناء» يشرح أن الربط يثبّت v1 لطلب الشاغر والعرضين. وصُحّح الأثر وأُلحقت ملاحظة التصحيح.
2. **G-WF-09:** صُحّح بند الإيفاد (المنشئ مسجَّل والمقارنة غائبة)، وأُلحقت ملاحظة التصحيح بالأثر.
3. **G-WF-12:** صُحّح بند «لا قفل تزامن» (xmin موجود على `Employee` و`ApprovalRequest` فقط)، وأُلحقت ملاحظة التصحيح بالأثر.
4. **G-WF-18 (P2):** أضيف نطاق السطر `155-211` لإجراء `Edit`.

## ملاحظات جديدة (لم تُضف إلى الدراسة)

- **السلفة عبر ESS تتجاوز الحد:** `EssAdvanceRequestModel.Amount` بلا تحقق (`EssPortalController.cs:203-207`). و`ProcessAdvanceRequestedHandler` ينشئ السلفة دون `ValidateAdvanceRequest` (`InboundEventCommands.cs:292-326`). و`ApproveAdvanceRequestHandler` لا يعيد فحص الحد (`AdvanceRequestHandlers.cs:121-156`). النتيجة: سلفة بأي مبلغ تُعتمد بشخص واحد. وهذا يجعل جزء السلفة في G-WF-08 قابلاً للبلوغ فعلاً.
- **العقوبة التأديبية لا تخصم من الراتب:** Compliance يرسل `PenaltyDays = 0` دائماً (`ComplianceExtendedControllers.cs:207-218`)، فلا يُنشئ Payroll خصماً للإيقاف. أما `Dismissal` فيعطّل ملف الأجر. هذا يخص G-ER-01 في دراسة علاقات العمل.
