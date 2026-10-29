# تحقق مستقل: COD_ORG_GAP_STUDY.md

**المدقق:** وكيل تحقق مستقل · **الشيفرة:** `/home/claude/hrm` @ `f9eab1a` · **المنهج:** فتح كل مرجع ملف/سطر لفجوات P0/P1 (ومعها G-COD-20 الموسومة P1)، والبحث عن أدلة مضادة في compose وk8s وسكربتات الترحيل والاختبارات، وفحص أرقام الأسطر فقط لـ P2/P3.

## جدول الأحكام

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-COD-01 | P0 | مؤكد | ست خدمات تستدعي `AddHrCodeGeneration`: Compliance وAdminLogistics وRecordsArchive وHRContracts وITInfra وIDCard. العنوان مضبوط لـ AdminLogistics وCompliance فقط، والنطاق `hr-core.workflow` لـ AdminLogistics فقط. ولا مفتاح بديل (`Services:HRCore:BaseUrl`/`HrCore:BaseUrl`) في أي `appsettings`. و`hr-app-config` في k8s بلا المفتاح. الإصلاح موجود في `docker-compose.acceptance.yml:99-105` وحده. |
| G-COD-02 | P0 | مؤكد | `CodePolicyTemplate : AuditableEntity` مملوك للمستأجر، والمرشح يلغي أثر `|| Scope == National`. الأدلة الحية `-2026-00031` و`-2026-00005` موجودة في الملفين المذكورين. |
| G-COD-03 | P0 | مؤكد | (أ) مركز الكلفة بلا تسلسل مع فهرس فريد على الإصدار `(TenantId, EntityKind, Code)` في ترحيل COD001. (ب) و(ج) و(هـ) مؤكدة بالشيفرة. (د) تبقى «يحتاج تحقق» كما في الدراسة. لا اختبار يستعمل `FundedPositionCodeService` الحقيقي. |
| G-COD-04 | P1 | مؤكد | `RecordIssuanceAsync` يحفظ السياق المشترك. `CodeIssuance` بلا كيان ولا حالة. الأمثلة الثلاثة تولّد الرقم قبل بناء الكيان. |
| G-COD-05 | P1 | مؤكد جزئياً | الثقة بـ `TenantId` من الجسم مؤكدة، وكذلك قدرة `hr-core` على حرق أرقام جهته. لكن RLS **مطبّق** على جداول الترميز في مسار النشر: `migrate-job.yaml:62` ← `run-migrations.sh:78` ← `ten002c-harden-tenant-rls.sql`، الذي يطبّق `FORCE RLS` و`USING`/`WITH CHECK` على كل جدول فيه `TenantId`. و`NpgsqlTenantSessionInterceptor` يضبط `app.current_tenant` عند فتح الاتصال. فالحرق العابر للجهات يُتوقَّع أن يفشل بخطأ قاعدة بيانات. يحسمه تشغيل: مستخدم جهة (أ) يرسل `TenantId` للجهة (ب) على قاعدة مُرحَّلة بالسكربت، ثم يُقرأ عدّاد (ب). |
| G-COD-06 | P1 | مؤكد | سبعة مواضع `if (reference is null) return`. `HireCaseCompletedConsumer` يسجّل تحذيراً، فليس صامتاً تماماً، لكن الرسالة تُؤكَّد بلا أرشفة. |
| G-COD-07 | P1 | مؤكد | الرقم يدوي قابل للتعديل، ولا فهرس على `DecisionNumber` في الكيان ولا في ترحيلات Contracts، و`AUTO-yyyyMMddHHmmss` في السطر 351. |
| G-COD-08 | P1 | مؤكد | لا كيان طلب تغيير هيكلي في أي مكان، و`EstablishmentChangeType` بلا عمليات وحدات. `AppendVersionAsync` يقبل `approvedBy`/`decisionReference` لكن لا مستدعٍ يمررهما. |
| G-COD-09 | P1 | مؤكد | لا فحص لـ `effectiveFrom` مقابل النسخة المفتوحة. الفهارس غير فريدة. لا xmin على `OrgUnit` ولا `FundedPosition`. الاستكمال يبدأ من `CreatedAt`. (ملف `OrgUnitVersionService.cs` بأسطر فارغة مزدوجة، والأرقام المذكورة صحيحة.) |
| G-COD-10 | P1 | مؤكد | `ParentId` يُحفظ في السطر 62 قبل `MoveSubtreeAsync`، والفحص يقرأ المسار القديم للأب. وفي MVC يُحفظ الاسم والرئيس قبل النقل، ويُرجَع View بلا نسخة ولا حدث عند الرفض. |
| G-COD-11 | P1 | مؤكد | الحدث بلا نسخة ولا تاريخ، والمستهلكون الستة يكتبون آخر رسالة تصل. `PublishAllOrgUnitsAsync` بلا `SaveChanges` («يحتاج تحقق» كما في الدراسة). |
| G-COD-12 | P1 | مؤكد | CRUD مباشر بلا أي فحص من فحوص الشاشة. |
| G-COD-13 | P1 | مؤكد | ملاحظة صغيرة: العدّ يشمل إدخالات الدليل غير النشطة أيضاً، فالشرط «لا إدخال دليل (نشطاً أو لا)». |
| G-COD-14 | P1 | مؤكد | لا ناشر لـ `IReferenceDataChanged`. والمزوّد يختار نسخة الجهة **أو** المنصة. |
| G-COD-15 | P2 | أسطر صحيحة | — |
| G-COD-16 | P2 | أسطر صحيحة | — |
| G-COD-17 | P2 | أسطر صحيحة | — |
| G-COD-18 | P2 | أسطر صحيحة | — |
| G-COD-19 | P3 | أسطر صحيحة | — |
| G-COD-20 | P1 | مؤكد | AC-13/14 في «Not yet executed». لا ملفات اختبار لـ AC-EST-07..09، ولا مجلدات أدلة لـ AC-EST-13..18. AC-13 `[SkippableFact]` بلا `[RequiredAcceptance]`، وAC-EST-14 يكتفي بأن الرمز غير فارغ. |

**الحصيلة (P0/P1):** مؤكد 14 · مؤكد جزئياً 1 (G-COD-05) · يحتاج تشغيلاً 0 · خطأ 0.

## التعديلات على الدراسة

1. **G-COD-05:** أعيدت كتابة بند RLS: لا `EnableRls` في الترحيلات، لكن سكربت TEN-002C في مسار النشر يغطي جداول الترميز. وصُحّح الأثر (الحرق العابر للجهات يُتوقَّع أن يفشل حيث طُبّق السكربت، والحرق داخل الجهة مؤكد)، وأُلحقت ملاحظة التصحيح.

## ملاحظات جديدة

- لا جديد جوهري. فشل إنشاء سجل الفصل في الإنتاج (`SeparationWizardController.Step1` ← `ApplyReferenceAsync`) نتيجة مباشرة لـ G-COD-01، ويسبق عائق G-POL-01 في المسار نفسه.
