# دراسة الأداء والتدريب والتعاقب والمنافع والصحة المهنية (TBH) — الوضع الفعلي وما بقي لإكماله

**تاريخ الدراسة:** 2026-10-29
**النطاق:** خمسة مسارات وتسليماتها إلى الخدمات الأخرى:
- **الأداء** في HR.Core: `EvaluationsController` و`PerformanceEvaluation`، وأثره على الترفيع والعلاوة (→ MOV / PAY).
- **التدريب والتعلم** في HR.Training: الدورات، الترشيح، التسجيل، الخطط، المزودون، النتائج، الشهادات، التعلم الإلكتروني وESS، البعثات.
- **التعاقب والمسار المهني** في HR.Core: `SuccessionPlans`، `KeyPositions`، `CareerPaths`، `WorkforcePlans`. استُبعد استخدام `TalentMarketplace` في التوظيف لأن دراسة HCM-001 غطّته.
- **المنافع والتقاعد والتأمينات:** `BenefitPlans` في HR.Core، والاشتراكات في Payroll (→ PAY)، وسجلات التقاعد الصحي وعقود وبطاقات التأمين في HR.Health، وتسليم التقاعد (→ SEP).
- **الصحة المهنية** في HR.Health: الملف الطبي، الموافقة، التوصيات، تقارير اللجنة الطبية، الفحوص الدورية، الحالة الصحية، السلامة المهنية. وربطها بالإجازة المرضية (→ LVA) وبإنهاء الخدمة لعدم اللياقة (→ SEP).
- ادعاءات FUN-004 (مصفوفة الرحلات وحزمة UAT) في جانبَي الصحة والتدريب. جانب الالتزام في FUN-004 خارج النطاق.

**المنهج:** قراءة ثابتة للشيفرة والاختبارات والوثائق في snapshot `main` @ `f9eab1a`، ومقارنتها بما تدّعيه الوثائق. تتبّعنا كل مسار من الواجهة حتى الحدث ومستهلكيه في الخدمات الأخرى.
**حدود الدراسة:** لم يُبنَ المشروع ولم تُشغَّل الاختبارات (لا يوجد .NET SDK في النسخة المدروسة). كل فجوة مدعومة بمرجع ملف/سطر تم التحقق منه. البنود الموسومة **«يحتاج تحقق»** استنتاجات تحتاج تشغيلاً لتأكيدها.
**الوثائق المقارَنة:** [HCM_003_PERFORMANCE.md](HCM_003_PERFORMANCE.md) · [HCM_004_BENEFITS.md](HCM_004_BENEFITS.md) · [HCM_005_LEARNING.md](HCM_005_LEARNING.md) · [TRN_001_TRAINING.md](TRN_001_TRAINING.md) · [TRN_002_UNIONS.md](TRN_002_UNIONS.md) · [FUN_004_JOURNEY_MATRIX.md](FUN_004_JOURNEY_MATRIX.md) · [FUN_004_UAT_PACK.md](FUN_004_UAT_PACK.md) · [FUN_004_EXCEPTION_PATHS.md](FUN_004_EXCEPTION_PATHS.md) · [FUN_004_RECONCILIATION_CHECKLIST.md](FUN_004_RECONCILIATION_CHECKLIST.md) · [FUN_004_VERIFICATION.md](FUN_004_VERIFICATION.md) · [ADR-FUN-004](ADR-FUN-004-health-training-compliance-closure.md) · [PRV_001_DPIA_HEALTH.md](PRV_001_DPIA_HEALTH.md) · [SYR_006_INSURANCE_PENSIONS.md](SYR_006_INSURANCE_PENSIONS.md) · [completion-backlog/09](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md) · [completion-backlog/04](completion-backlog/04-P3-QUALITY-GOVERNANCE-FUNCTIONS.md) · [Requariments.md](Requariments.md)

---

## 1. الخلاصة

1. **المسارات الخمسة موجودة كشاشات CRUD وأحداث، لكن لا يكتمل أي منها كمسار حكومي محكوم.** المنجز الحقيقي جزئي:
   - الملف الطبي محمي بسياسة سريرية.
   - بطاقة التأمين لها دورة حياة، وتُنهى في saga الفصل.
   - التسجيل المباشر في دورة ينتهي بشهادة قابلة للتحقق.
   - خطط القوى العاملة محكومة بنطاق مركزي.

   أما الأداء فسجل واحد حر (رقم 0–100 وحالة `draft/final`). والمنافع جدول للعرض فقط لا تقرؤه الرواتب. و`HCM-003/004/005` معلنة «Done» بأدلة كلها نماذج `SharedKernel` غير مستخدمة في التشغيل، وهو النمط نفسه الذي وجدته HCM-001 في G-23.
2. **أربعة عوائق P0:**
   - **(أ)** المسار السريري متوقف في الواجهة: لا يوجد نموذج لتسجيل الموافقة، وكل تعديل للملف الطبي يمحوها. فلا تصدر توصية ولا قرار لجنة إلا بطلب مصنوع يتجاوز الموافقة (G-TBH-01).
   - **(ب)** توصية طبيب واحد بـ`Retirement` تحوّل الموظف في HR.Core إلى `Retired`، وهي حالة نهائية، دون قرار ولا ملف إنهاء خدمة. وتوصية `FitnessForDuty` تعيد موظفاً موقوفاً إلى `Active`. وقرار لجنة «مهام محدودة» يفتح ملف إنهاء خدمة طبي. ويكفي الدور غير السريري `hr-health` لإصدار قرار اللجنة (G-TBH-02).
   - **(ج)** البيانات السريرية مكشوفة: نتائج اللجنة والفحوص الدورية متاحة لـ`hr-health` بلا نطاق فرع. ونص النتائج السريرية يُنسخ إلى سجل الحالة في HR.Core وإلى ملف الإنهاء في HR.Contracts وإلى عنوان عنصر الأرشيف. ولا يوجد التشفير الحقلي الذي تدّعيه DPIA (G-TBH-03).
   - **(د)** المسار المحكوم للتدريب ميت: اعتماد الترشيح ينشئ تسجيلاً بلا checklist، فلا يمكن إتمامه أبداً. المسار الوحيد العامل هو التسجيل المباشر بلا اعتماد (G-TBH-04).
3. **ثغرات سلامة وحوكمة (P1):**
   - اعتمادات التدريب والبعثات بلا فصل مهام وبلا هوية المعتمد (`ApprovedBy = Guid.Empty`)، والبعثة تولّد بدلاً في الرواتب بنقرة واحدة.
   - التعلم الذاتي في ESS غير مكشوف للموظفين عبر البوابة. ومن يصل إليه يُكمل الدورة بإرسال `100%`، ويستطيع تكرار النشر للحصول على علاوة تدريب في كل مرة.
   - شهادة تصدر لمن رسب، ورقمها قابل للتخمين، وحذف دورة إلكترونية يحذف شهاداتها.
   - التقييم النهائي قابل للتعديل والحذف، والقائمة بلا نطاق فرع.
   - الاشتراكات في الرواتب نسبة ثابتة من الإجمالي بلا سقف ولا أهلية. وملف التأمينات بلا رقم وطني، ويخلط التأمين الصحي بالاجتماعي، وتصديره يعيد إرسال ملف الصرف.
   - حذف صلب لسجلات صحية وتأمينية، خلافاً لـ FUN-004.
   - بيانات التعاقب مفتوحة لكل أدوار HR.Core.
   - ادعاءات «Done/ReadyForSign-off» بلا دليل.
4. **فجوات قدرات (P2):**
   - دورة تقييم وتظلم وأثر على الترفيع والعلاوة.
   - ميزانية التدريب وحضوره وتعهّد الخدمة بعد البعثة.
   - اشتراك في المنافع ومعالون، وإحالة معاش، ورصد سن التقاعد.
   - حملة الفحص السنوي، وربط الإجازة المرضية وإصابة العمل.
   - ربط المناصب الحرجة والمسارات بالملاك.
5. **فجوة تحقق:**
   - لا يوجد أي سيناريو قبول ولا مجلد أدلة لهذا المجال.
   - اختبار E2E الوحيد يفحص رفض المجهول فقط، وفيه اختبار شكلي للشخصية السريرية.
   - اختبار منع ربط الكيانات (mass assignment) لا يحمّل `HR.Health.Web` ولا `HR.Training.Web`، فيفلت منه 34 إجراءً يربط كياناً مباشرة.
6. **النمط المكرر من PLAN-25:** لا شيء في هذا المجال يعتمد على `IsLabScale` أو وضع محرك السياسات. المشكلة هنا عكسية: **«تطبيق عند الحفظ»** في كل مكان، بلا مسار اعتماد ولا محرك سياسات:
   - القرار الطبي يغيّر الحالة فوراً.
   - البعثة تولّد مالاً فوراً.
   - الترشيح يُعتمد بنقرة من صاحبه.

   قاعدتا السياسة `R-INS-MASK` و`R-TRN` مبذورتان، لكن الخدمتين لا تستدعيان المحرك أصلاً.

**التقدير الإجمالي:** نحو 74–97 يوم عمل لمطوّر واحد (≈ 5 sprints لفريق من مطوّرَين). منها 23–29 يوماً للمرحلة P0 (TBH-A وTBH-B وTBH-C).

---

## 2. خريطة المسار كما هو في الشيفرة

| # | المرحلة | التنفيذ الحالي | الحكم |
|---|---------|----------------|-------|
| 1 | إدخال تقييم سنوي | `EvaluationsController` — رقم 0–100 وحالة نصية `draft/final` يدخلهما HR | ⚠️ بلا مقيِّم ولا نطاق ولا تثبيت بعد الاعتماد (G-TBH-08) |
| 2 | دورة الأداء، المعايرة، التظلم، الأثر على الترفيع/العلاوة | لا يوجد. `PerformanceCycle/Review` في SharedKernel غير مستخدمة | ❌ (G-TBH-13، G-TBH-12) |
| 3 | خطة تدريب سنوية | `TrainingPlans` + `Approve` بنقرة، `ApprovedBy = Guid.Empty` | ⚠️ (G-TBH-05) |
| 4 | ترشيح ← اعتماد ← تسجيل ← إتمام | `TrainingNominations.Approve` ينشئ تسجيلاً بلا checklist | ❌ لا يُتمّ أبداً (G-TBH-04) |
| 5 | تسجيل مباشر ← إتمام ← شهادة ← `ITrainingCompleted` | `TrainingEnrollments.Create/Complete` | ⚠️ يعمل لكنه يتجاوز الاعتماد، وبلا علامة نجاح (G-TBH-07) |
| 6 | تعلم إلكتروني ذاتي (ESS) | `EssLearningController` | ❌ مغلق بالبوابة أمام الموظف، وإتمام ذاتي (G-TBH-06) |
| 7 | التحقق من الشهادة | `POST /api/certificates/verify` | ⚠️ داخلي فقط، رقم قابل للتخمين (G-TBH-07) |
| 8 | البعثات | `MissionFiles` ← `IMissionApproved` ← بدل في Payroll | ⚠️ اعتماد ذاتي بلا تعهد (G-TBH-05، G-TBH-14) |
| 9 | التعاقب والمناصب الحرجة والمسارات المهنية | CRUD في HR.Core | ⚠️ مفتوح لكل أدوار الوحدة (G-TBH-11، G-TBH-17) |
| 10 | خطط القوى العاملة | `WorkforcePlans` + نطاق مركزي + `GapReport` | ✅ مع اعتماد بخطوة واحدة (G-TBH-17) |
| 11 | خطط المنافع | `BenefitPlans` عرض فقط، ببذرة 7%/11% | ⚠️ لا مستهلك (G-TBH-09، G-TBH-15) |
| 12 | الاشتراكات ← الرواتب ← ملف التأمينات | `StatutoryDeductionRule` + `PayrollExternalExportService` | ❌ (G-TBH-09) |
| 13 | الملف الطبي والموافقة | إنشاء تلقائي عند التعيين + `RecordConsent` بلا واجهة | ❌ (G-TBH-01) |
| 14 | التوصية الطبية / قرار اللجنة | ← حالة HR.Core + ملف إنهاء في HR.Contracts + تسوية في Payroll | ❌ (G-TBH-02، G-TBH-03) |
| 15 | الفحوص الدورية، الحالة الصحية، ملف التقاعد الصحي | CRUD بلا سياسة سريرية ولا نطاق | ⚠️ (G-TBH-03، G-TBH-10، G-TBH-16) |
| 16 | بطاقات وعقود التأمين الصحي | دورة حياة + مسودة في saga التعيين + إنهاء في saga الفصل | ⚠️ حذف صلب وتعديل بعد التفعيل (G-TBH-10) |
| 17 | التقاعد ← إنهاء الخدمة / المعاش | `HealthRetirementRecords` بلا أثر. لا رصد لسن التقاعد ولا إحالة معاش | ❌ (G-TBH-02، G-TBH-15) |
| 18 | السلامة المهنية وحوادث العمل | CRUD، حذف صلب، أيام مرضية كرقم | ⚠️ (G-TBH-10، G-TBH-16) |
| 19 | الإجازة المرضية (LVA) | لا ربط بالملف الطبي | ❌ (G-TBH-16) |

---

## 3. ما هو منجز فعلاً (للإنصاف)

- **الملف الطبي نفسه محمي جيداً:** كل إجراءات `MedicalRecordsController` تحمل `HrHealthClinical`. ونطاق الفرع مطبَّق على `Index/Summary/Details`. والعرض يُسجَّل في `SensitiveDataAudit`. والإلغاء ناعم. والحقول السريرية موسومة `[AuditRedact]` ([MedicalRecordsController.cs:32-109](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecordsController.cs)، [MedicalRecord.cs:14-28](../src/Services/HRHealth/HR.Health.Domain/Entities/MedicalRecord.cs)).
- **قاعدة الموافقة صحيحة كدالة صرفة:** الغرض، والانتهاء، والسحب، والإلغاء. ولها اختبار وحدة ([HealthPrivacyRules.cs:7-20](../src/Services/HRHealth/HR.Health.Domain/Services/HealthPrivacyRules.cs)).
- **دورة حياة بطاقة التأمين صريحة.** وفي saga التعيين تُنشأ مسودة، وفي saga الفصل تُلغى مع تعويض يعيد تفعيلها ([PartyAckConsumers.cs:25-130](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs)).
- **التسجيل المباشر يعمل حتى الشهادة:** checklist بفهرس فريد `(EnrollmentId, Step)`، وتسجيل ملف الشهادة في FILE-002، وواجهة تحقق برقم الشهادة. وفي HR.Core يُنشأ مؤهل للموظف من `ITrainingCompleted`، مع منع التكرار لكل دورة.
- **سلامة التعلم الإلكتروني في القاعدة:** التحاق واحد لكل موظف ودورة، ورقم شهادة فريد ([TrainingDbContext.cs:119-129](../src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs)).
- **الرسائل:** الخدمتان تستخدمان Outbox على PostgreSQL ومرشّح Inbox عاماً. ومستهلكا HR.Core للأحداث الطبية يمنعان التكرار بعلامة في سجل الحالة.
- **تصدير التقييمات مقيّد بنطاق الفرع ومدقَّق** ([ReportsController.cs:278-287](../src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs)).
- **خطط القوى العاملة:** كتابة مركزية فقط، ونطاق، ومرجع قانوني عند الاعتماد، وتقرير فجوة ([WorkforcePlansController.cs:47-189](../src/Services/HRCore/HR.Core.Web/Controllers/WorkforcePlansController.cs)).
- **حماية عامة:** `TenantIdStampInterceptor` يفرض الجهة عند الإدراج، فيحدّ من أثر ربط الكيانات عبر الجهات. والبوابة تفرض `HrCoreWrite` على كل POST في `hr-core`، فلا يكتب في التعاقب والمسارات إلا `hr-core` أو `admin`.
- **TRN-001 وTRN-002 وثيقتا برنامج، لا وحدة HRTraining:** الأولى منهج تدريب المستخدمين، والثانية تمثيل العاملين. لا تعدان بأي شيفرة، فلا فجوة شيفرة فيهما. لكن TRN-002 يشترط استشارة الممثلين قبل تغيير معايير التقييم ومتطلبات التدريب ([TRN_002_UNIONS.md:29-33](TRN_002_UNIONS.md))، وهذا مدخل في D-TBH-6.

---

## 4. الفجوات بالتفصيل

تصنيف الأولوية: **P0** يمنع الاستخدام الحقيقي أو الإصدار · **P1** ثغرة سلامة/حوكمة في القرار أو المال · **P2** قدرة ناقصة · **P3** تحسين.

### 4.1 عوائق P0

#### G-TBH-01 — المسار السريري متوقف في الواجهة: لا تسجيل للموافقة، والتعديل يمحوها
- **الدليل:**
  - لا يوجد نموذج يستدعي `RecordConsent` أو يرسل `emergencyOverride` في أي واجهة. البحث في `src/Services/HRHealth/HR.Health.Web/Views` عن `Consent` و`emergencyOverride` بلا نتيجة. الإجراء موجود في الخادم فقط: [MedicalRecordsController.cs:155-175](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecordsController.cs).
  - الملف الطبي يُنشأ تلقائياً عند `IEmployeeCreated` بلا موافقة: [EmployeeCreatedConsumer.cs:22-30](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/EmployeeCreatedConsumer.cs).
  - التوصية وقرار اللجنة يُرفضان دون موافقة سارية: [MedicalRecommendationsController.cs:52-56](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs)، [HealthExtendedControllers.cs:54-59](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs).
  - `Edit` POST ينسخ حقول الموافقة الأربعة من النموذج المرسَل ([MedicalRecordsController.cs:146-149](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecordsController.cs))، لكن نموذج التعديل لا يحويها ([Edit.cshtml:6-13](../src/Services/HRHealth/HR.Health.Web/Views/MedicalRecords/Edit.cshtml)). لذلك **كل تعديل للملف يمحو الموافقة**. وبطلب POST مصنوع يحدث العكس: تُثبَّت موافقة بتاريخ رجعي دون أثر في `SensitiveDataAudit`، لأن التدقيق موجود في `RecordConsent` وحده (:172).
  - بيانات العرض وحدها فيها موافقة ([DemoHealthSeeder.cs:140-142](../src/Services/HRHealth/HR.Health.Infrastructure/Data/DemoHealthSeeder.cs))، لذلك يبدو المسار عاملاً في العروض.
  - FUN-004 يجعل `ConsentRecorded` خطوة إلزامية بمهلة 24 ساعة ([FUN_004_JOURNEY_MATRIX.md:9](FUN_004_JOURNEY_MATRIX.md))، ويعلن U2 «Clinical + Consent → Recommendation» جاهزاً للتوقيع ([FUN_004_UAT_PACK.md:6](FUN_004_UAT_PACK.md)).
- **الأثر:** في جهة حقيقية لا يصدر الطبيب أي توصية ولا قرار لجنة من الواجهة. يبقى طريقان فقط: طلب مصنوع بـ`emergencyOverride=true` بلا سبب، أو كتابة الموافقة عبر `Edit`. أي أن ضابط الخصوصية الوحيد إما يوقف العمل وإما يُتجاوز بسهولة.
- **الإصلاح المقترح:**
  - نموذج موافقة في تفاصيل الملف: الغرض، المدة، مستند موقّع عبر FILE-002، والسحب.
  - سجل موافقات مؤرخ وغير قابل للتعديل بدل الحقول الأربعة.
  - نموذج عرض (view model) لـ`Edit` لا يمس الموافقة.
  - التجاوز الطارئ بواجهة وسبب إلزامي وفق D-TBH-3.
- **التحقق المطلوب:** AC-TBH-Aa: الشخصية `a-clinician` تسجّل موافقة من الواجهة ثم تصدر توصية. تعديل الملف بعدها لا يغيّر الموافقة. سالب: POST إلى `Edit` بحقول موافقة يُتجاهل. توصية بلا موافقة تُرفض. وتجاوز بلا سبب يُرفض.

#### G-TBH-02 — قرار طبي فردي يغيّر حالة الموظف نهائياً، و«مهام محدودة» تفتح إنهاء خدمة
- **الدليل:**
  - **توصية فردية:** توصية `Retirement` من طبيب واحد، لا من لجنة، تُنشر كـ`IMedicalRecommendationIssued` ([MedicalRecommendationsController.cs:66-76](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs)). فيحوّل HR.Core الموظف إلى `Retired` فوراً ([MedicalRecommendationIssuedConsumer.cs:50-66](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalRecommendationIssuedConsumer.cs)، [MedicalEmploymentEffectRules.cs:10-19](../src/Services/HRCore/HR.Core.Domain/Services/MedicalEmploymentEffectRules.cs)).
    - `Retired` حالة نهائية لا تنتقل إلا إلى `Anonymized` ([EmploymentStatusRules.cs:43](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)).
    - HR.Contracts لا يستهلك هذا الحدث، فلا ملف إنهاء خدمة ولا قرار إداري.
  - **إعادة التفعيل:** `FitnessForDuty` تحوّل الموظف إلى `Active`. والانتقال `Suspended → Active` و`Seconded → Active` مسموح ([EmploymentStatusRules.cs:28-41](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)). فتوصية لياقة واحدة تعيد موظفاً موقوفاً تأديبياً أو منتدباً إلى «على رأس العمل».
  - **قرار اللجنة:** قرار `Retirement` يكرر الأثر نفسه عبر `IMedicalDecisionIssued` ([MedicalDecisionIssuedConsumer.cs:51-70](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalDecisionIssuedConsumer.cs)). وفي الوقت ذاته يفتح HR.Contracts `SeparationRecord` من نوع `Medical` ([InboundEventConsumers.cs:139-150](../src/Services/HRContracts/HR.Contracts.Infrastructure/Consumers/InboundEventConsumers.cs)، [InboundEventCommands.cs:360-396](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)).
    - عند إتمام الإنهاء يحوّل HR.Core النوع `Medical` إلى `Terminated` ([EmploymentStatusRules.cs:77-89](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)). لكن الانتقال `Retired → Terminated` مرفوض، فتختلف الحالة بين الخدمتين (يحتاج تحقق).
  - **«مهام محدودة»:** `ShouldPublishDecision` يشمل `LimitedDuties` ([MedicalDecisionWorkflow.cs:15-17](../src/Services/HRHealth/HR.Health.Domain/Services/MedicalDecisionWorkflow.cs)). ومستهلك HR.Contracts لا يميّز `MedicalRef`، فـ**قرار «مهام محدودة» يفتح ملف إنهاء خدمة**. وفي HR.Core يُضاف سجل توظيف «مهام محدودة» بنوع `Permanent` ثابت، خارج مسار التحركات ([MedicalDecisionIssuedConsumer.cs:83-93](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalDecisionIssuedConsumer.cs)).
  - **لا نشر لتغيّر الحالة:** المستهلكان الطبيان في HR.Core لا ينشران تغيّر الحالة. أما مسار الإنهاء النظامي فيستدعي `PublishStatusChangedIfNeededAsync` ([SeparationCompletedConsumer.cs:79-84](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/SeparationCompletedConsumer.cs)). فلا تعلم الرواتب ولا الدليل بالتغيير.
  - **من يصدر قرار اللجنة:**
    - `MedicalCommitteeReportsController` بلا أي `[Authorize(Policy=…)]` ([HealthExtendedControllers.cs:15](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs))، فيكفي الدور غير السريري `hr-health`.
    - الكيان بلا أعضاء ولا نصاب ([MedicalCommitteeReport.cs:8-31](../src/Services/HRHealth/HR.Health.Domain/Entities/MedicalCommitteeReport.cs)).
    - `emergencyOverride` معامل منطقي (bool) بلا سبب ([HealthExtendedControllers.cs:39, 54-61](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
  - **التعديل بعد القرار:** تعديل التقرير يغيّر `Recommendation` دون إلغاء الأثر ودون إعادة النشر ([HealthExtendedControllers.cs:130-138](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
  - **التعويض المالي:**
    - توصية `Compensation` تنشئ `PayrollAdjustment` تُصرف في أول تشغيل رواتب لاحق ([InboundEventHandlers.cs:683-715](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
    - المبلغ أول رقم في نص حر ([MedicalCompensationRules.cs:11-24](../src/Services/Payroll/Payroll.Domain/Services/MedicalCompensationRules.cs)).
    - لكن HR.Health يرسل `Details = string.Empty` دائماً ([MedicalRecommendationsController.cs:70](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs))، فالمبلغ هو القيمة الافتراضية في الإعداد دائماً.
- **الأثر:**
  - مستخدم صحي واحد ينهي فعلياً خدمة موظف في السجل المرجعي، دون قرار إداري ودون اعتماد، وبلا رجعة.
  - أو يعيد موظفاً موقوفاً إلى العمل.
  - أو يفتح ملف إنهاء لموظف أوصت اللجنة بتخفيف مهامه.
  - ويولّد صرفاً مالياً من نص طبي.
  - هذا يناقض [Requariments.md:37](Requariments.md) («قرار لجنة طبية» سبب إنهاء يُنفَّذ «وفقاً لأنظمة ولوائح العمل») و[Requariments.md:65](Requariments.md) (مهام مكتبية، مهام محدودة، تعويضات، كمسارات منفصلة).
- **الإصلاح المقترح:**
  - لا تغيير حالة مباشر من HR.Health.
  - قرار اللجنة المعتمد وحده ينشر حدثاً، والتوصية الفردية لا أثر وظيفي لها.
  - الحدث يفتح طلباً في HR.Contracts (إنهاء طبي أو قرار إداري) يعتمده HR. والحالة في HR.Core تتغير عند `SeparationCompleted` فقط.
  - «مهام محدودة» تفتح طلب تكليف أو نقل (→ MOV)، لا إنهاء خدمة.
  - التعويض يمر بقرار معتمد ومبلغ صريح، لا بنص حر.
  - سياسة `HrHealthClinical` على اللجنة، مع أعضاء ونصاب (D-TBH-1/2).
- **الخطة:** [PLAN-61](acceptance-plans/PLAN-61-sep-legal-types-single-entry.md) (جزئياً: لا حالة نهائية من المستهلكين الطبيين، و«مهام محدودة» لا تفتح فصلاً، وقرار التقاعد الطبي يفتح طلب فصل `Draft` يمر بالاعتماد). والتوصية الفردية واللجنة ونصابها والتعويض و`FitnessForDuty`: TBH-A
- **التحقق المطلوب:**
  - AC-TBH-Ab: توصية فردية `Retirement` لا تغيّر حالة الموظف.
  - AC-TBH-Ac: قرار لجنة `Retirement` معتمد ينشئ طلب إنهاء معلّقاً، وحالة HR.Core تبقى `Active` حتى إتمامه، ثم تصبح `Retired` مع نشر الحدث.
  - AC-TBH-Ad: «مهام محدودة» لا تنشئ `SeparationRecord`.
  - سالب: `a-health` يحاول إنشاء تقرير لجنة فيُرفض بـ403. و`FitnessForDuty` لموظف `Suspended` لا تغيّر حالته.

#### G-TBH-03 — البيانات السريرية مكشوفة للدور غير السريري، وبلا نطاق، وتنتقل نصاً إلى ثلاث خدمات
- **الدليل:**
  - **الأدوار:**
    - `ModuleHrHealth` يقبل `hr-health`، و`HrHealthClinical` يقبل `hr-health-clinical` و`admin` فقط ([HrManagementAuthorizationExtensions.cs:32-38](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)).
    - لا يحمل السياسة السريرية إلا `MedicalRecords` و`MedicalRecommendations`.
    - المتاح لـ`hr-health` (الشخصية `a-health` في [personas.json:197-207](../testdata/acceptance/personas.json)):
      - تقارير اللجنة بنتائجها السريرية `Findings` ([HealthExtendedControllers.cs:15-27, 101-109](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)، [Details.cshtml:18](../src/Services/HRHealth/HR.Health.Web/Views/MedicalCommitteeReports/Details.cshtml)).
      - الفحوص الدورية بقياساتها `Vitals` ([PeriodicMedicalChecksController.cs:9-12](../src/Services/HRHealth/HR.Health.Web/Controllers/PeriodicMedicalChecksController.cs)).
      - الحالة الصحية وملف التقاعد الصحي ([HealthExtendedControllers.cs:208-211, 260-263](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
  - **نطاق الفرع:**
    - مطبّق على `Index/Summary/Details` للملف الطبي فقط.
    - غائب عن `Edit` و`RecordConsent` و`Cancel` ([MedicalRecordsController.cs:111-193](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecordsController.cs)).
    - غائب عن قائمة التوصيات وتفاصيلها ([MedicalRecommendationsController.cs:26-30, 84-92](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs)).
    - غائب عن كل متحكمات الصحة الأخرى.
    - لا يوجد مستهلك `TransferExecuted` في HR.Health، بخلاف HR.Training (`TransferExecutedConsumer.cs`). لذا تبقى لقطة فرع الموظف على فرع التعيين ([EmployeeCreatedConsumer.cs:32-47](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)).
  - **النص السريري يعبر الحدود:** `Notes = model.Findings` ([HealthExtendedControllers.cs:90](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)) يصل إلى ثلاث خدمات:
    - HR.Core يكتبه في `EmploymentStatusHistory.Reason` ([MedicalDecisionIssuedConsumer.cs:51-52](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalDecisionIssuedConsumer.cs)). يظهر في شاشة سجل الحالة ([Index.cshtml:59](../src/Services/HRCore/HR.Core.Web/Views/StatusHistory/Index.cshtml)) وفي تصدير CSV ([ReportsController.cs:242-270](../src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs)) لكل أدوار HR.Core.
    - HR.Contracts يكتبه في `SeparationRecord.Reason` ([InboundEventCommands.cs:379](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)).
    - Records.Archive يكتبه في عنوان عنصر الأرشيف ([MedicalArchiveConsumers.cs:50](../src/Services/RecordsArchive/Records.Archive.Infrastructure/Consumers/MedicalArchiveConsumers.cs)).
  - **التشفير:** DPIA تدّعي «field encryption» ([PRV_001_DPIA_HEALTH.md:8](PRV_001_DPIA_HEALTH.md)). لكن `HealthDbContext` بلا أي محوّل تشفير ([HealthDbContext.cs:35-45](../src/Services/HRHealth/HR.Health.Infrastructure/Data/HealthDbContext.cs)). و`[AuditRedact]` يحجب الحقول في سجل التدقيق فقط.
  - **اختبار UAT في المكان الخطأ:** U1 يختبر `hr-core` ([FUN_004_UAT_PACK.md:5](FUN_004_UAT_PACK.md))، وهذا الدور لا يدخل وحدة الصحة أصلاً. أما الدور الذي يثبت الفصل فهو `hr-health`، ولا يُختبر.
- **الأثر:** بيانات صحية من الفئة الخاصة (القانون 12/2024، DPIA) تصل إلى:
  - موظفي الصحة غير السريريين في كل الفروع.
  - كل مستخدمي HR.Core عبر سجل الحالة.
  - مستخدمي العقود والأرشيف.

  معيار القبول الأول في FUN-004 («السجل الطبي لا يظهر لأدوار HR العامة») غير متحقق. وهذا مانع إصدار من جهة حماية البيانات.
- **الإصلاح المقترح:**
  - `HrHealthClinical` على اللجنة والفحوص والحالة الصحية.
  - نطاق فرع على كل قائمة وتفاصيل وإجراء.
  - مستهلك `TransferExecuted` في HR.Health.
  - أحداث بلا نص سريري: رمز القرار ومرجعه فقط.
  - تنظيف النص المنسوخ سابقاً في الخدمات الثلاث بسكربت ترحيل.
  - تشفير الحقول السريرية (D-TBH-4).
- **الخطة:** [PLAN-52](acceptance-plans/PLAN-52-iam-branch-scope-bola.md) (جزئياً: مستهلك `TransferExecuted` في HR.Health). والسياسة السريرية والنطاق والأحداث بلا نص سريري والتشفير: TBH-B
- **التحقق المطلوب:**
  - AC-TBH-Ba: `a-health` يفتح تفاصيل تقرير لجنة وفحصاً دورياً فيُرفض بـ403.
  - AC-TBH-Bb: `a-clinician` من فرع آخر يُرفض.
  - AC-TBH-Bc: بعد قرار لجنة، فحص قواعد HR.Core وHR.Contracts وArchive لا يجد نص `Findings`.
  - AC-TBH-Bd: بعد نقل الموظف، يراه طبيب الفرع الجديد ولا يراه طبيب القديم.

#### G-TBH-04 — مسار التدريب المحكوم ميت: الترشيح المعتمد لا يمكن إتمامه
- **الدليل:**
  - `TrainingNominations.Approve` يضيف `TrainingEnrollment` مباشرة دون إنشاء `TrainingChecklist` ([TrainingExtendedControllers.cs:272-323](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)، الإضافة في :291-298).
  - الـchecklist يُنشأ ويُعلَّم فقط في `TrainingEnrollments.Create` ([TrainingEnrollmentsController.cs:61-67](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingEnrollmentsController.cs)). البحث عن `TrainingChecklistDefaults.` لا يجد استدعاءً آخر.
  - `Complete` يرفض دون خطوة `Enrolled` مكتملة بدليل ([TrainingEnrollmentsController.cs:125-130](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingEnrollmentsController.cs)، [TrainingChecklistDefaults.cs:31-35](../src/Services/HRTraining/HR.Training.Domain/Services/TrainingChecklistDefaults.cs)). ولا توجد واجهة لتعليم الخطوة يدوياً.
  - «الدليل» في المسار المباشر يُولَّد ذاتياً (`enrollment:{id}`، :66)، فالبوابة شكلية حتى هناك.
  - التسجيل المباشر لا يمنع تكرار الموظف في الدورة نفسها ([TrainingEnrollmentsController.cs:47-59](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingEnrollmentsController.cs))، بخلاف `Approve` (:284).
  - الاستثناء «إتمام تدريب بلا خطوة Enrolled → رفض» موثّق ([FUN_004_EXCEPTION_PATHS.md:8](FUN_004_EXCEPTION_PATHS.md))، لكن لم يُلاحظ أنه يقتل مسار الترشيح.
- **الأثر:** كل موظف رُشّح واعتُمد يبقى `Enrolled` إلى الأبد: لا نتيجة، ولا شهادة، ولا مؤهل في HR.Core. وللإنجاز يضطر المسؤول إلى تسجيل مباشر جديد يتجاوز الترشيح والاعتماد. هذا يكرر نمط G-03/G-07 في HCM-001: المسار السليم مستحيل، فيُدفع المستخدم إلى الالتفاف.
- **الإصلاح المقترح:**
  - خدمة تسجيل واحدة تستخدمها المسارات الثلاثة: الترشيح، والمباشر، وESS. تنشئ الـchecklist وتمنع التكرار.
  - دليل حقيقي لخطوة `Enrolled`: رقم الترشيح المعتمد أو قرار الإيفاد.
  - التسجيل المباشر يتطلب ترشيحاً معتمداً، أو سبباً موثقاً بصلاحية أعلى.
- **التحقق المطلوب:** AC-TBH-Ca: ترشيح ← اعتماد من شخصية ثانية ← إتمام بدرجة ناجحة ← شهادة قابلة للتحقق ← مؤهل في HR.Core. سالب: تسجيل مكرر للموظف نفسه في الدورة نفسها يُرفض.

### 4.2 ثغرات سلامة القرار والحوكمة (P1)

#### G-TBH-05 — اعتمادات التدريب والبعثات بلا فصل مهام ولا هوية معتمد، والبعثة تولّد مالاً
- **الدليل:**
  - **الصلاحيات:** كل متحكمات HR.Training، عدا `EssLearning` و`Certificates`، بلا `[Authorize(Policy=…)]`، وتعتمد على مرشّح الوحدة فقط. والوحدة لها دور واحد `hr-training` ([HrManagementAuthorizationExtensions.cs:40](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs))، وشخصية قبول واحدة ([personas.json:183-195](../testdata/acceptance/personas.json)).
  - **الترشيح:** `TrainingNominations.Approve` لا يقارن المعتمد بالمرشِّح. و`NominatedBy` لا يُسند من الخادم، بل يأتي من النموذج المرسَل ([TrainingExtendedControllers.cs:244-259](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)، [TrainingNomination.cs:20](../src/Services/HRTraining/HR.Training.Domain/Entities/TrainingNomination.cs)).
  - **الخطة السنوية:** اعتمادها يكتب `p.ApprovedBy = Guid.Empty` ([TrainingExtendedControllers.cs:74](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
  - **البعثة:**
    - ينشئها ويعتمدها المستخدم نفسه، فيُنشر `IMissionApproved` ([TrainingExtendedControllers.cs:398-421](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
    - Payroll يضيف `PayrollAdjustment` بقيمة `MissionAllowanceAmount` تُصرف في التشغيل التالي ([InboundEventHandlers.cs:541-569](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
    - ولا يمنع المعالج التكرار بالعلامة `Mission-{id}`، بخلاف المعالج الطبي.
  - **محرك العمل:** لا يوجد تعريف مسار عمل لأي من هذه الاعتمادات. وقاعدة `R-TRN` المبذورة لا تُستدعى (انظر التقاطعات).
- **الأثر:** موظف تدريب واحد يرشّح ويعتمد ويوفد ويولّد صرفاً، دون أثر لهويته في الاعتماد. هذا يخالف فصل المهام الذي تفرضه بقية المنظومة، ويخالف «الترشيح والتكلفة مدققان» في HCM-005.
- **الإصلاح المقترح:**
  - دور `hr-training-approver` (D-TBH-11) وسياسات صريحة على كل POST.
  - فحص على الخادم أن المعتمد ليس المنشئ.
  - هوية المعتمد من المطالبات (claims) لا من النموذج.
  - البعثة عبر مسار اعتماد بخطوتين يربط قراراً إدارياً.
  - البدل لا يُصرف إلا بقرار معتمد (D-TBH-8).
- **التحقق المطلوب:** AC-TBH-Cb: المرشِّح نفسه يعتمد فيُرفض. شخصية ثانية تعتمد فينجح، ويُسجَّل `ApprovedBy` الصحيح. AC-TBH-Cc: اعتماد بعثة لا يولّد تسوية رواتب قبل القرار الإداري، وإعادة نشر الحدث لا تكرر التسوية.

#### G-TBH-06 — التعلم الذاتي (ESS): مغلق أمام الموظفين بالبوابة، وإتمامه ذاتي ويكرر علاوة التدريب
- **الدليل:**
  - **البوابة:** `TryGetEssPolicy` يمنح `ModuleEss` لمسارات `/Ess` تحت `payroll` و`hr-core` و`ess` فقط ([GatewayPathAuthorizationMiddleware.cs:240-258](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)). المسار `/ui/hr-training/EssLearning` يقع تحت سياسة `ModuleHrTraining`، فيُرفض الموظف العادي بـ403. ولا رابط إليه من بوابة الخدمة الذاتية: البحث عن `EssLearning` خارج HR.Training لا يجد إلا اختبار معمارية.
  - **الإتمام الذاتي:** من يصل، أي مستخدم يحمل `hr-training` و`hr-employee` معاً، يرسل `UpdateProgress(progressPercent=100)`. فيُعلَّم الإتمام وتصدر شهادة، ويُنشر `ITrainingCompleted` بـ`Score = 100m` ([EssLearningController.cs:89-132](../src/Services/HRTraining/HR.Training.Web/Controllers/EssLearningController.cs)). ولا تحقق من الدروس ولا من مدة.
  - **التكرار:** كل استدعاء لاحق بـ100% ينشر الحدث من جديد (:121، بلا شرط «لم يكن مكتملاً»). ومعالج Payroll يحسب علامة التكرار ولا يفحصها: `var marker = …` ثم `AddAsync` مباشرة ([InboundEventHandlers.cs:656-675](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)). فتُضاف علاوة تدريب مع كل استدعاء، متى فُعّلت `EnableTrainingPremiums` ([PayrollConfiguration.cs:65-68](../src/Services/Payroll/Payroll.Domain/Entities/PayrollConfiguration.cs)). والتفعيل متاح من شاشة إعداد الرواتب.
- **الأثر:**
  - الميزة التي تعدها FUN-004 («ESS Enroll») غير متاحة لجمهورها.
  - ومن يصل إليها يصدر لنفسه شهادات ومؤهلات في الملف المرجعي.
  - ويضاعف علاوة مالية بلا حد.
- **الإصلاح المقترح:**
  - ربط المسار بـ`ModuleEss` في البوابة (→ IAM)، ورابط من بوابة الخدمة الذاتية.
  - الإتمام من تقدّم الدروس المسجّل على الخادم، أو من اختبار.
  - النشر مرة واحدة عند الانتقال إلى `Completed`.
  - فحص العلامة في معالج Payroll وفهرس فريد للتسوية (→ PAY).
- **التحقق المطلوب:** AC-TBH-Da: موظف `hr-employee` يفتح التعلم الذاتي عبر البوابة. AC-TBH-Db: إرسال 100% دون إكمال الدروس يُرفض. عشرة استدعاءات بعد الإتمام تنتج حدثاً واحداً وتسوية واحدة على الأكثر.

#### G-TBH-07 — سجل التدريب والشهادات: شهادة للراسب، رقم قابل للتخمين، والحذف يمحو الشهادات
- **الدليل:**
  - **لا علامة نجاح:** `Complete` يضبط `Completed` ويولّد `CertificateNo` أياً كانت الدرجة، ولا حد لقيمتها ([TrainingEnrollmentsController.cs:148-165](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingEnrollmentsController.cs)). والكيان `TrainingCourse` بلا علامة نجاح ([TrainingCourse.cs:8-35](../src/Services/HRTraining/HR.Training.Domain/Entities/TrainingCourse.cs)).
  - **رقم الشهادة:**
    - الصيغة `CERT-{رمز الدورة}-{yyyyMMdd}-{أول 6 محارف من معرّف الموظف}` ([LearningCompletionRules.cs:24-29](../src/Services/HRTraining/HR.Training.Domain/Services/LearningCompletionRules.cs)). قابلة للتخمين، وغير فريدة لشهادات القاعة: لا فهرس على `TrainingEnrollment.CertificateNo` ([TrainingDbContext.cs:56-64](../src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs)).
    - واجهة التحقق ترجع `EmployeeId` لكل مستخدم يملك دور الوحدة ([CertificatesController.cs:10-13, 41-42, 51-52](../src/Services/HRTraining/HR.Training.Web/Controllers/CertificatesController.cs)). ولا يصل إليها متحقق خارجي.
  - **الحذف الصلب:**
    - `ELearningCourses.Delete` حذف صلب ([ELearningCoursesController.cs:114-124](../src/Services/HRTraining/HR.Training.Web/Controllers/ELearningCoursesController.cs)) يتتالى إلى الالتحاقات ثم الشهادات ([TrainingDbContext.cs:103-106, 121-124](../src/Services/HRTraining/HR.Training.Infrastructure/Data/TrainingDbContext.cs)). فتصبح شهادة صادرة «NotFound» عند التحقق.
    - حذف صلب لرموز التأهيل ولمزوّدي التدريب أيضاً ([TrainingExtendedControllers.cs:131-141, 219-229](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
- **الأثر:**
  - شهادات ومؤهلات لمن رسب، تصل إلى ملف الموظف في HR.Core.
  - تزوير برقم مُخمَّن، مع كشف هوية حامل الشهادة.
  - ضياع شهادات صادرة بحذف إداري.

  هذا يناقض «الشهادة قابلة للتحقق» في FUN-004 و«certificate fraud» في تحقق HCM-005.
- **الإصلاح المقترح:**
  - علامة نجاح لكل دورة، وحالة `Failed` بلا شهادة.
  - رقم عشوائي فريد مع فهرس.
  - تحقق عام بأقل بيانات وبلا معرّف الموظف (D-TBH-10).
  - إبطال الشهادة بدل حذفها، وأرشفة ناعمة للدورات ورموز التأهيل والمزوّدين.
- **التحقق المطلوب:** AC-TBH-Cd: درجة دون علامة النجاح فلا شهادة ولا `ITrainingCompleted` بنجاح. AC-TBH-Ce: حذف دورة إلكترونية لها شهادات يُرفض أو يؤرشفها، والتحقق يبقى `Valid`. سالب: التحقق برقم مُخمَّن لموظف آخر لا يعيد معرّفه.

#### G-TBH-08 — سجل التقييم: بلا نطاق، والنهائي قابل للتعديل والحذف، وبلا مقيِّم
- **الدليل:**
  - **النطاق:**
    - `Index` يطبّق نطاق الفرع فقط حين يُمرَّر `employeeId`. بدونه يعرض تقييمات الجهة كلها ([EvaluationsController.cs:36-56](../src/Services/HRCore/HR.Core.Web/Controllers/EvaluationsController.cs)).
    - `Edit` و`Delete` بلا أي فحص نطاق (:159-216).
    - أما التصدير فمقيّد ([ReportsController.cs:278-287](../src/Services/HRCore/HR.Core.Web/Controllers/ReportsController.cs)).
  - **التعديل:** `Edit` يكتب `evaluation.Status = vm.Status` من قائمة `draft/final` (:194، [Edit.cshtml:34-37](../src/Services/HRCore/HR.Core.Web/Views/Evaluations/Edit.cshtml)). فالنهائي يعود مسودة وتتغير درجته دون أثر.
  - **الحذف:** صلب (:211).
  - **المقيِّم:** لا يُسند `EvaluatorId` عند الإنشاء (:141-148).
  - **التفرد:** لا قيد فريد على `(EmployeeId, Year)` ([HRCoreDbContext.cs:286-292](../src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs)).
  - **المستهلك:** التعاقب يقرأ أحدث تقييم بالسنة أياً كانت حالته، مسودة أو مكرراً ([SuccessionPlansController.cs:85-89](../src/Services/HRCore/HR.Core.Web/Controllers/SuccessionPlansController.cs)).
  - **وعود HCM-003:** «النموذج محفوظ بعد الإرسال» و«المدير مقيّد تاريخياً» ([HCM_003_PERFORMANCE.md:88-90](HCM_003_PERFORMANCE.md)).
- **الأثر:**
  - مدير فرع أو معتمد يقرأ تقييمات الفروع الأخرى.
  - موظف `hr-core` يعدّل تقييماً نهائياً بعد اعتماده.
  - تصنيف جاهزية التعاقب يُبنى على مسودات.
- **الإصلاح المقترح:**
  - نطاق على القائمة والتعديل والحذف.
  - آلة حالات: `Draft → Submitted → Final`، والنهائي غير قابل للتعديل إلا بتصحيح مدقَّق.
  - المقيِّم من المطالبات (claims)، وقيد فريد لكل موظف ودورة، وحذف ناعم.
- **التحقق المطلوب:** AC-TBH-Ea: مدير فرع لا يرى تقييمات فرع آخر في القائمة ولا يفتح `Edit`. AC-TBH-Eb: تعديل تقييم `Final` يُرفض، وإنشاء تقييم ثانٍ للسنة نفسها يُرفض.

#### G-TBH-09 — تسليم الاشتراكات إلى الرواتب وملف التأمينات غير صالحين (بالاشتراك مع PAY)
- **الدليل:**
  - **خطط المنافع للعرض فقط:**
    - `BenefitPlans` شاشة عرض فقط ([BenefitPlansController.cs:11-33](../src/Services/HRCore/HR.Core.Web/Controllers/BenefitPlansController.cs)). لا يقرؤها أي كود آخر.
    - بذرتها «للقبول والعرض» تعمل ضمن الحزمة الوطنية الإلزامية في الإنتاج بنسب 7%/11% ([BenefitPlanSeeder.cs:8-15, 39-40](../src/Services/HRCore/HR.Core.Infrastructure/Data/BenefitPlanSeeder.cs)، [NationalSeedOrchestrator.cs:54, 93](../src/Services/HRCore/HR.Core.Infrastructure/Seeding/NationalSeedOrchestrator.cs)، تُستدعى عند كل إقلاع دون شرط ملف البذر في [Program.cs:294](../src/Services/HRCore/HR.Core.Web/Program.cs)).
  - **الاستقطاع الإلزامي في الرواتب:**
    - نسبة ثابتة من **إجمالي** الأجر، بلا سقف ولا أجر خاضع ولا أهلية حسب نظام الخدمة ([PayrollRules.cs:126-152](../src/Services/Payroll/Payroll.Domain/Services/Engine/Rules/PayrollRules.cs)).
    - `DeductionType` بلا مرجع قانوني ولا تاريخ نفاذ ([DeductionType.cs:7-25](../src/Services/Payroll/Payroll.Domain/Entities/DeductionType.cs)).
  - **ثلاث مجموعات نسب مختلفة مثبتة في الكود:**
    - 7%/11% في البذرة.
    - 7%/14% ثوابت في التصدير ([PayrollExternalExportService.cs:19-20](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)).
    - 7%/14% بسقف 3,000,000 في الخريطة ([insurance-jurisdiction-map.json:63-65](syr/insurance-jurisdiction-map.json)). لا يقرأ الكود هذه الخريطة، ولا يطبّق أي سقف.
  - **ملف التأمينات:**
    - **التصنيف بالاسم:** حصة العامل = مجموع أي خصم يحوي اسمه «تأمين» أو «Insurance» ([PayrollExternalExportService.cs:84-89](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)). هذا يجمع «تأمين صحي» مع التأمينات الاجتماعية ([DemoPayrollSeeder.cs:58-60](../src/Services/Payroll/Payroll.Infrastructure/Data/DemoPayrollSeeder.cs)).
    - **التعويض الافتراضي:** إن لم يوجد خصم، يُبلَّغ 7% من الإجمالي لم تُقتطع فعلاً (:91-92). وحصة الجهة 14% ثابتة (:94).
    - **التعريف:** `NationalId: null` في كل سطر (:98)، والملف يحمل معرّف GUID داخلياً ([FileSocialInsuranceReporter.cs:18-28](../src/Shared/HRManagement.Integration.FileAdapters/Insurance/FileSocialInsuranceReporter.cs)). ولا حقل لرقم المؤمَّن عليه في أي كيان.
    - **الاقتران بالصرف:** التصدير يرسل ملف الصرف أيضاً (:128-129)، ويفشل كله إن نقص حساب مصرفي واحد (:105-110). والإجراء `ExportSocialInsuranceFile` من نوع GET ([PayrollReportsController.cs:274-276](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)).
  - **وعود الوثائق:** [legal-source-binding.md:91](syr/legal-source-binding.md) يعد بربط نسب التأمين بالمرسوم 135. و[HCM_004_BENEFITS.md:85](HCM_004_BENEFITS.md) يعلن «Totals reconcile with Payroll and provider» منجزاً.
- **الأثر:** الملف الشهري إلى المؤسسة العامة للتأمينات الاجتماعية لا يطابق أي مؤمَّن عليه، ويبلّغ مبالغ خاطئة:
  - يخلط التأمين الصحي بالاجتماعي.
  - يبلّغ عن اشتراك لم يُقتطع.
  - يتجاوز السقف.

  واستقطاع الموظف نفسه بلا سقف ولا أهلية. وكل تصدير للتأمينات يعيد إرسال تعليمات الصرف.
- **الإصلاح المقترح:**
  - خطة منافع مؤرخة ومعتمدة هي المصدر الوحيد للنسب والسقف والأجر الخاضع ونظام الخدمة، وتقرؤها قاعدة الاستقطاع في الرواتب (D-TBH-7).
  - رقم وطني أو رقم تأمين في الملف.
  - تصنيف المكوّنات برمز لا بالاسم.
  - فصل تصدير التأمينات عن الصرف وجعله POST.
  - تقرير مصالحة: مجموع الاستقطاع يساوي مجموع الملف.
- **التحقق المطلوب:** AC-TBH-Ga: تشغيل رواتب بخطة 7%/14% وسقف ← خصم العامل محسوب على الأجر الخاضع حتى السقف، والملف يطابق مجموع الخصومات سطراً سطراً ويحمل المعرّف الوطني. سالب: لا خطة نافذة لنظام الموظف ← لا استقطاع وتنبيه. تصدير التأمينات لا ينشئ ملف صرف.

#### G-TBH-10 — سجلات الصحة والتأمين: حذف صلب، وتغيير صاحب السجل، وتعديل بعد التفعيل
- **الدليل:**
  - **حذف صلب:**
    - عقد التأمين ([HealthExtendedControllers.cs:195-205](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
    - الفحص الدوري ([PeriodicMedicalChecksController.cs:61-71](../src/Services/HRHealth/HR.Health.Web/Controllers/PeriodicMedicalChecksController.cs)).
    - حادث العمل ([WorkplaceSafetyController.cs:66-76](../src/Services/HRHealth/HR.Health.Web/Controllers/WorkplaceSafetyController.cs)).

    وكلها خلافاً لـ«لا hard delete لسجلات طبية/تأمين» ([FUN_004_RECONCILIATION_CHECKLIST.md:13](FUN_004_RECONCILIATION_CHECKLIST.md)).
  - **تغيير صاحب السجل:** `Edit` يغيّر `EmployeeId` في:
    - الملف الطبي ([MedicalRecordsController.cs:140](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecordsController.cs)).
    - التوصية ([MedicalRecommendationsController.cs:113](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs)).
    - ملف التقاعد والحالة الصحية ([HealthExtendedControllers.cs:250, 302](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
    - بطاقة تأمين `Active` ([InsuranceCardsController.cs:65-90](../src/Services/HRHealth/HR.Health.Web/Controllers/InsuranceCardsController.cs)). ويمكن أيضاً تمديد `ExpiryDate` لبطاقة نشطة دون أثر.
  - **تكرار البطاقات:** لا فحص لبطاقة نشطة أخرى للموظف عند الإنشاء ([InsuranceCardsController.cs:33-43](../src/Services/HRHealth/HR.Health.Web/Controllers/InsuranceCardsController.cs)).
  - **ربط الكيانات:** 34 إجراءً في HR.Health وHR.Training يربط الكيان مباشرة من النموذج (انظر G-TBH-12).
- **الأثر:**
  - يُنقل ملف طبي كامل إلى موظف آخر بتعديل واحد.
  - تضيع أدلة حوادث العمل التي تحتاجها مطالبات إصابة العمل.
  - تتعدد البطاقات النشطة للموظف الواحد.
- **الإصلاح المقترح:**
  - نماذج عرض بلا `EmployeeId` في التعديل.
  - إلغاء ناعم بدل الحذف.
  - قفل تعديل البطاقة بعد التفعيل إلا عبر إجراءات دورة الحياة.
  - قيد «بطاقة نشطة واحدة لكل موظف ومزوّد».
- **التحقق المطلوب:** AC-TBH-Be: POST إلى `Edit` بـ`EmployeeId` مختلف لا يغيّر صاحب السجل. حذف عقد تأمين أو حادث يصبح إلغاءً ناعماً. بطاقة نشطة ثانية تُرفض.

#### G-TBH-11 — بيانات التعاقب والمواهب مفتوحة لكل أدوار HR.Core وعبر الفروع
- **الدليل:**
  - **القراءة:** `SuccessionPlansController` و`KeyPositionsController` و`CareerPathsController` بلا سياسة صريحة ولا نطاق فرع ([SuccessionPlansController.cs:12-37](../src/Services/HRCore/HR.Core.Web/Controllers/SuccessionPlansController.cs)، [KeyPositionsController.cs:21-38](../src/Services/HRCore/HR.Core.Web/Controllers/KeyPositionsController.cs)). فكل دور في `ModuleHrCore` يقرأ كل الخطط والمرشحين وتصنيف جاهزيتهم ([HrManagementAuthorizationExtensions.cs:13-22](../src/Shared/HRManagement.ServiceDefaults/HrManagementAuthorizationExtensions.cs)): مدير الفرع، والمعتمد، والإشرافي، والتنفيذي.
  - **القوائم:** قائمة الموظفين في نموذج الإضافة تشمل الجهة كلها (:14-20).
  - **الإضافة:** `AddCandidate` يقبل الدرجة وسنوات الخدمة من المستخدم (:80-101)، فتُتجاوز بيانات التقييم والخدمة.
  - **الحذف:** صلب للخطط والمرشحين (:107-131).
  - **الكتابة:** إجراءات POST الأحد عشر لهذه المتحكمات مجمّدة في قائمة الاستثناء ([hrcore-post-policy-allowlist.txt:14-17, 34-36, 48-51](../tests/architecture/hrcore-post-policy-allowlist.txt)).
  - **الوعد:** HCM-005 يعلن «Succession access restricted» منجزاً ([HCM_005_LEARNING.md:91](HCM_005_LEARNING.md)).
- **الأثر:** تصنيفات المواهب والتعاقب حساسة بطبيعتها، وتطّلع عليها أدوار لا شأن لها بها. وتصنيف «جاهز الآن» يُصنع بإدخال يدوي.
- **الإصلاح المقترح:**
  - سياسة قراءة مقيّدة (`HrCoreTalent`) ونطاق فرع.
  - الدرجة من آخر تقييم `Final` وسنوات الخدمة من سجل التوظيف على الخادم.
  - حذف ناعم، وإزالة الإجراءات من قائمة الاستثناء.
- **التحقق المطلوب:** AC-TBH-Ha: `branch-manager` يُرفض على `SuccessionPlans/Index`. AC-TBH-Hb: إرسال درجة يدوية في `AddCandidate` يُتجاهل، والجاهزية تُحسب من التقييم النهائي.

#### G-TBH-12 — ادعاءات «Done» و«ReadyForSign-off» بلا دليل، والحراس المعمارية لا تغطي الخدمتين
- **الدليل:**
  - **أدلة إغلاق غير مستخدمة:** بطاقات HCM-003/004/005 «Done» بأدلة إغلاق كلها نماذج SharedKernel وسكربت تحقق وجود ملفات ([09-NATIONAL-FUNCTIONAL-CAPABILITIES.md:118-161](completion-backlog/09-NATIONAL-FUNCTIONAL-CAPABILITIES.md)، [hcm003-validate.ps1:22-23](../scripts/hcm003-validate.ps1)).
    - `PerformanceCycle` و`PerformanceReview` و`PensionEnrollment` و`SkillRecord` و`SharedKernel.Talent.LearningEnrollment` لا يستخدمها إلا الاختبارات. البحث عن `SharedKernel.Performance|Talent` خارج `Tests` لا يجد إلا الملفات نفسها.
    - و`Hcm003PerformanceTests` اختبارات انعكاس (reflection) عليها ([Hcm003PerformanceTests.cs:3, 23-40](../src/Tests/HRManagement.UnitTests/Hcm003PerformanceTests.cs)).
  - **FUN-004:** معلنة «Done» وحزمة UAT «ReadyForSign-off» ([FUN_004_VERIFICATION.md:13](FUN_004_VERIFICATION.md)، [FUN_004_UAT_PACK.md:14](FUN_004_UAT_PACK.md)). لكن:
    - E2E يفحص رفض المجهول فقط.
    - «اختبار» الشخصية السريرية يتحقق من وجود `hr-admin` ([Fun004HealthTrainingComplianceE2ETests.cs:28-37](../src/Tests/HRManagement.E2E/Fun004HealthTrainingComplianceE2ETests.cs)).
    - لا يوجد مجلد `acceptance-evidence/AC-TBH-*` ولا AC منصة لهذا المجال ([acceptance-evidence/summary.md](../acceptance-evidence/summary.md)).
  - **اختبار ربط الكيانات:** `MassAssignmentArchitectureTests` يحاول تحميل `HR.Health.Web` ويتخطاه بصمت عند غيابه، ولا يذكر `HR.Training.Web` أصلاً ([MassAssignmentArchitectureTests.cs:41-60](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs)). ومشروع الاختبار لا يرجع إلى أي منهما. فيفلت 34 إجراءً يربط الكيان مباشرة.
- **الأثر:** قرار الإطلاق يُبنى على «Done» لا يغطي الشيفرة الفعلية. وثلاثة من أربعة عوائق P0 هنا كانت ستُكشف بسيناريو قبول واحد بشخصيتين.
- **الإصلاح المقترح:**
  - تصحيح الوثائق لتصف الكيانات الفعلية.
  - حذف نماذج SharedKernel غير المستخدمة أو ربطها.
  - سيناريوهات `AC-TBH-*` حية بـ`AcceptanceGate`.
  - إضافة المشروعين إلى حارس ربط الكيانات وحارس سياسة POST.
- **التحقق المطلوب:** AC-TBH-Va: مجلد أدلة لكل خطة بالعناصر الثمانية. حارس المعمارية يفشل إذا رُبط كيان في HR.Health أو HR.Training. لا تبقى عبارة «Done» دون مرجع لاختبار سلوك أو دليل قبول.

### 4.3 قدرات ناقصة (P2)

#### G-TBH-13 — لا دورة أداء ولا تظلم ولا أثر على الترفيع أو العلاوة
- **الدليل:**
  - **الكيان:** `PerformanceEvaluation` فيه: سنة، ودرجة، وملخص، وحالة نصية ([PerformanceEvaluation.cs:6-30](../src/Services/HRCore/HR.Core.Domain/Entities/PerformanceEvaluation.cs)). لا دورة ولا أهداف ولا كفاءات ولا تقييم ذاتي ولا معايرة ولا تظلم ولا إقرار من الموظف.
  - **الإدخال:** الكتابة لـ`HrCoreWrite` فقط، فالرئيس المباشر (مدير الفرع) لا يقيّم.
  - **ESS:** لا تعرض بوابة الخدمة الذاتية أي تقييم: البحث عن «evaluation» في `EssPortalController` بلا نتيجة.
  - **الترفيع:** `PromotionRequest` لا يقرأ التقييم ([PromotionRegimeValidationService.cs:9-16](../src/Services/HRMovements/HR.Movements.Infrastructure/Services/PromotionRegimeValidationService.cs)).
  - **العلاوة الدورية:** لا يوجد أي منطق لها في Payroll. البحث عن `increment` بلا نتيجة وظيفية.
  - **المتطلب:** [Requariments.md:13](Requariments.md) يطلب «تقييم الأداء السنوي بشكل رقمي مؤتمت».
- **الأثر:** لا تستطيع الجهة تشغيل دورة التقييم السنوية، ولا ربطها بالترفيع والعلاوة كما يفرض نظام العاملين.
- **الإصلاح المقترح:**
  - دورة v1 وفق D-TBH-6: تقييم الرئيس المباشر ضمن نطاقه، ثم اعتماد، ثم إقرار الموظف في ESS، ثم تظلم بلجنة.
  - عرض آخر تقييم نهائي في طلب الترفيع كتنبيه.
  - الأثر المالي الآلي مؤجل حتى D-TBH-5.
- **التحقق المطلوب:** AC-TBH-Fa: دورة كاملة من فتحها حتى إقرار الموظف. AC-TBH-Fb: تظلم يغيّر الدرجة بقرار لجنة مع حفظ النسخة الأصلية.

#### G-TBH-14 — التدريب: لا ميزانية ولا حضور ولا تعهّد خدمة ولا ربط بالوظيفة أو بالفصل
- **الدليل:**
  - **الكيانات:**
    - الدورة بلا كلفة ولا سعة ولا تواريخ ([TrainingCourse.cs:8-35](../src/Services/HRTraining/HR.Training.Domain/Entities/TrainingCourse.cs)).
    - الخطة نص `jsonb` بلا ميزانية ([TrainingPlan.cs:8-26](../src/Services/HRTraining/HR.Training.Domain/Entities/TrainingPlan.cs)).
    - البعثة بلا تعهد ولا أثر على حالة الموظف ([MissionFile.cs:9-38](../src/Services/HRTraining/HR.Training.Domain/Entities/MissionFile.cs)).
    - رمز التأهيل غير مربوط بدليل الوظائف ([QualificationCode.cs:8-24](../src/Services/HRTraining/HR.Training.Domain/Entities/QualificationCode.cs)).
  - **saga الفصل:** HR.Training ليس طرفاً فيها ([ProcessSagaCommands.cs:4-20](../src/Shared/HRManagement.MessageContracts/Commands/ProcessSagaCommands.cs)). فتبقى الترشيحات والبعثات مفتوحة بعد الفصل، ولا يُسترد التعهد.
  - **المهل:** `SlaHours` مخزّنة ولا تقرؤها أي مهمة، ولا مهام خلفية في الخدمة.
  - **الوعود:** HCM-005 يعلن «IsWithinBudget» منجزاً ([HCM_005_LEARNING.md:92](HCM_005_LEARNING.md)). والمتطلبات تذكر كود التأهيل لكل وظيفة وقسم البعثات ([Requariments.md:93-106, 242-248](Requariments.md)).
- **الإصلاح المقترح:**
  - كلفة وميزانية مع مصالحة.
  - حضور يومي.
  - تعهّد خدمة بمرجع قانوني (D-TBH-9) يُسترد عند الفصل (→ SEP/PAY).
  - ربط رمز التأهيل بـ`JobCatalogEntry`.
  - طرف تدريب في saga الفصل.
- **التحقق المطلوب:** AC-TBH-Ka: ترشيح يتجاوز الميزانية يُرفض. AC-TBH-Kb: فصل موظف ملتزم بتعهد ينتج مطالبة استرداد.

#### G-TBH-15 — المنافع والمعاش والتقاعد: لا اشتراك ولا معالون ولا إحالة معاش ولا رصد لسن التقاعد
- **الدليل:**
  - **لا اشتراك ولا معالون:** لا كيان اشتراك موظف في خطة. `PensionEnrollment` غير مستخدم. ولا نموذج للمعالين: أفراد الأسرة نص حر في البطاقة ([InsuranceCard.cs:27-28](../src/Services/HRHealth/HR.Health.Domain/Entities/InsuranceCard.cs)).
  - **ملف التقاعد الصحي:** يُنشأ يدوياً بلا أي أثر ([HealthExtendedControllers.cs:215-224](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)).
  - **سن التقاعد:** لا رصد له. الخريطة تذكر 60 سنة ([insurance-jurisdiction-map.json:71](syr/insurance-jurisdiction-map.json))، والبحث عن `RetirementAge` بلا نتيجة.
  - **إحالة المعاش:** لا ملف إحالة إلى المؤسسة العامة للتأمين والمعاشات، ولا حساب لمدة الخدمة.
  - **بطاقات التأمين:**
    - لا ترتبط بفرع الهويات للطباعة ([Requariments.md:66](Requariments.md)).
    - saga الفصل تلغي بطاقة المتقاعد ([PartyAckConsumers.cs:45-61](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/PartyAckConsumers.cs))، بينما يطلب [Requariments.md:67](Requariments.md) تنظيم عقود تأمين للمحالين على التقاعد.
- **الإصلاح المقترح:**
  - اشتراك مؤرخ في الخطط مع معالين منظَّمين.
  - مهمة رصد بلوغ السن تفتح طلب تقاعد في HR.Contracts (→ SEP).
  - ملف إحالة معاش.
  - تمييز التقاعد عن الإنهاء في saga الفصل.
- **التحقق المطلوب:** AC-TBH-Ia: موظف يبلغ السن خلال 90 يوماً يظهر في قائمة الرصد، ويُفتح طلبه. AC-TBH-Ib: التقاعد يُبقي تغطية المتقاعد وفق العقد.

#### G-TBH-16 — الصحة المهنية: لا حملة فحص سنوي، ولا ربط بالإجازة المرضية أو بإصابة العمل
- **الدليل:**
  - **الفحص السنوي:** `NextDue` في الفحص الدوري لا يقرؤه شيء، ولا مهام خلفية في HR.Health. والمتطلب فحص سنوي لكامل القوى البشرية ([Requariments.md:62](Requariments.md)).
  - **الإجازة المرضية:** محكومة بحد 15 يوماً ثابت فقط ([LeaveAndAdvanceRules.cs:182](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)). لا تتطلب تقريراً طبياً، ولا تحيل الإجازة الطويلة إلى اللجنة.
  - **إصابة العمل:** `RelatedSickLeaveDays` في حادث العمل رقم بلا أثر ([WorkplaceAccident.cs:34-36](../src/Services/HRHealth/HR.Health.Domain/Entities/WorkplaceAccident.cs)). ولا إخطار لإصابة العمل، وهي من خدمات المؤسسة العامة للتأمينات الاجتماعية في الخريطة نفسها.
  - **بطاقات التأمين:** `MarkExpired` يدوي فقط.
- **الإصلاح المقترح:**
  - حملة فحص سنوية لكل فرع مع قائمة متأخرين.
  - الإجازة المرضية فوق حد معيّن تتطلب تقريراً طبياً، وتُحال إلى اللجنة بعد مدة (→ LVA).
  - حادث العمل ينتج إخطار إصابة.
  - مهمة انتهاء البطاقات.
- **التحقق المطلوب:** AC-TBH-Ja: إجازة مرضية فوق الحد دون تقرير تُرفض. AC-TBH-Jb: بطاقة منتهية تُعلَّم آلياً.

#### G-TBH-17 — المناصب الحرجة والمسارات وخطط القوى العاملة غير مربوطة بالملاك
- **الدليل:**
  - **المناصب الحرجة:** `KeyPosition` عنوان حر وشاغل يُدخل يدوياً، بلا ربط بـ`FundedPosition` ([KeyPosition.cs:7-28](../src/Services/HRCore/HR.Core.Domain/Entities/KeyPosition.cs)). فلا يتحدث الشاغل عند النقل أو الفصل.
  - **المسارات المهنية:** خطوات `CareerPathStep` مسميات حرة. `JobClassificationCode` اختياري ([CareerPathStep.cs:15-30](../src/Services/HRCore/HR.Core.Domain/Entities/CareerPathStep.cs)).
  - **خطط القوى العاملة:** يعتمد خطة القوى العاملة منشئها نفسه بخطوة واحدة ([WorkforcePlanService.cs:77-98](../src/Services/HRCore/HR.Core.Infrastructure/Services/Establishment/WorkforcePlanService.cs)).
- **الإصلاح المقترح:**
  - ربط المنصب الحرج بالمنصب الممول، والشاغل من الإشغال الفعلي.
  - خطوات المسار من دليل الوظائف.
  - فصل منشئ خطة القوى العاملة عن معتمدها.
- **التحقق المطلوب:** AC-TBH-Hc: نقل شاغل منصب حرج يحدّث الشاغل. منشئ خطة القوى العاملة لا يعتمدها.

### 4.4 تحسينات (P3)

#### G-TBH-18 — رسائل نجاح مشوّهة الترميز في شاشة التقييم
- **الدليل:** `TempData["Success"]` في [EvaluationsController.cs:153, 198, 214](../src/Services/HRCore/HR.Core.Web/Controllers/EvaluationsController.cs) نص عربي مُرمَّز مرتين (`ØªÙ… Ø¥Ø¶Ø§ÙØ©…`)، كما تُظهر البايتات `303 230 302 252`.
- **الأثر:** المستخدم يرى رموزاً غير مفهومة بعد كل حفظ.
- **الإصلاح المقترح:** إعادة النص العربي الصحيح، واختبار يمنع `Ã`/`Ø` في الشيفرة.
- **التحقق المطلوب:** ضمن AC-TBH-Ea: الرسالة بعد الحفظ عربية مقروءة.

### 4.5 تقاطعات مع الدراسات الأخرى

- **→ IAM:**
  - متحكمات HR.Health وHR.Training بلا `[Authorize(Policy=…)]`، والبوابة لا تفصل القراءة عن الكتابة إلا في `hr-core` ([GatewayPathAuthorizationMiddleware.cs:132-134](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - `TryGetEssPolicy` لا يشمل `hr-training` ([GatewayPathAuthorizationMiddleware.cs:247-252](../src/Shared/HRManagement.ServiceDefaults/GatewayPathAuthorizationMiddleware.cs)).
  - HR.Health بلا مستهلك `TransferExecuted`، فلقطة الفرع قديمة ([EmployeeCreatedConsumer.cs:32-47](../src/Services/HRHealth/HR.Health.Infrastructure/Consumers/EmployeeCreatedConsumer.cs)).
  - 11 إجراء POST للمواهب (CareerPaths وKeyPositions وSuccessionPlans) في قائمة الاستثناء ([hrcore-post-policy-allowlist.txt:14-17, 34-36, 48-51](../tests/architecture/hrcore-post-policy-allowlist.txt)).
- **→ POL:** قاعدتا `R-INS-MASK` و`R-TRN` مبذورتان ([PolicyEngineDomainSeeds.cs:112-118, 152-158](../src/Services/HRCore/HR.Core.Infrastructure/Data/PolicyEngineDomainSeeds.cs)). عميل المحرك مسجّل في الخدمتين ([HR.Health Program.cs:38](../src/Services/HRHealth/HR.Health.Web/Program.cs)، [HR.Training Program.cs:36](../src/Services/HRTraining/HR.Training.Web/Program.cs))، لكن لا يُستدعى في أي موضع. القاعدتان يتيمتان.
- **→ WF:** لا تعريف مسار عمل لاعتماد الترشيح أو البعثة أو الخطة أو قرار اللجنة الطبية. كلها اعتمادات محلية بنقرة ([TrainingExtendedControllers.cs:67-79, 272-323, 398-421](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs)).
- **→ QA:**
  - حارس ربط الكيانات لا يحمّل `HR.Health.Web` ولا `HR.Training.Web` ([MassAssignmentArchitectureTests.cs:41-60](../src/Tests/HRManagement.UnitTests/MassAssignmentArchitectureTests.cs)).
  - اختبار الشخصية السريرية شكلي ([Fun004HealthTrainingComplianceE2ETests.cs:28-37](../src/Tests/HRManagement.E2E/Fun004HealthTrainingComplianceE2ETests.cs)).
  - `Hcm003/004/005*Tests` تفحص نماذج غير مستخدمة.
- **→ OPS:**
  - النشر في معاملتين منفصلتين: حفظ الكيان، ثم `Publish`، ثم حفظ ثانٍ يدفع الـOutbox. مواضعه: [TrainingEnrollmentsController.cs:174-191](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingEnrollmentsController.cs)، [MedicalRecommendationsController.cs:61-77](../src/Services/HRHealth/HR.Health.Web/Controllers/MedicalRecommendationsController.cs)، [HealthExtendedControllers.cs:81-95](../src/Services/HRHealth/HR.Health.Web/Controllers/HealthExtendedControllers.cs)، [TrainingExtendedControllers.cs:406-418](../src/Services/HRTraining/HR.Training.Web/Controllers/TrainingExtendedControllers.cs).
  - هذا النمط مسموح صراحة في [OutboxPublishArchitectureTests.cs:37-51](../src/Tests/HRManagement.UnitTests/OutboxPublishArchitectureTests.cs).
  - لا مهام خلفية في الخدمتين.
- **→ PAY:**
  - تصدير التأمينات GET يعيد إرسال ملف الصرف ([PayrollReportsController.cs:274-276](../src/Services/Payroll/Payroll.Web/Controllers/PayrollReportsController.cs)، [PayrollExternalExportService.cs:128-129](../src/Services/Payroll/Payroll.Infrastructure/Integration/PayrollExternalExportService.cs)).
  - علامة علاوة التدريب محسوبة ولا تُفحص ([InboundEventHandlers.cs:665](../src/Services/Payroll/Payroll.Application/Features/InboundEvents/InboundEventHandlers.cs)).
  - مبلغ التعويض الطبي من نص حر ([MedicalCompensationRules.cs:11-24](../src/Services/Payroll/Payroll.Domain/Services/MedicalCompensationRules.cs)).
- **→ SEP:**
  - `MapSeparationType` يحوّل `Medical` إلى `Terminated` ([EmploymentStatusRules.cs:77-89](../src/Services/HRCore/HR.Core.Domain/Services/EmploymentStatusRules.cs)).
  - طلب الإنهاء الوارد يحفظ ملاحظات سريرية في `Reason` ([InboundEventCommands.cs:379](../src/Services/HRContracts/HR.Contracts.Application/Features/InboundEvents/InboundEventCommands.cs)).
- **→ LVA:** الحد الأقصى للإجازة المرضية 15 يوماً مثبت في الكود، وبلا تقرير طبي ([LeaveAndAdvanceRules.cs:182](../src/Services/HRContracts/HR.Contracts.Domain/Services/LeaveAndAdvanceRules.cs)).
- **→ MOV:**
  - الترفيع لا يقرأ التقييم ([PromotionRequest.cs:8-51](../src/Services/HRMovements/HR.Movements.Domain/Entities/PromotionRequest.cs)).
  - «مهام محدودة» تُكتب كسجل توظيف من مستهلك، خارج مسار التحركات ([MedicalDecisionIssuedConsumer.cs:83-93](../src/Services/HRCore/HR.Core.Infrastructure/Consumers/MedicalDecisionIssuedConsumer.cs)).

---

## 5. قرارات مطلوبة من صاحب العمل

| # | القرار | الخيار الموصى به | يؤثر على |
|---|--------|------------------|----------|
| D-TBH-1 | الأثر الوظيفي للتوصية الطبية وقرار اللجنة | لا تغيير حالة من HR.Health. قرار اللجنة المعتمد يفتح طلب إنهاء أو قرار إداري في HR.Contracts، والحالة تتغير عند `SeparationCompleted` فقط. «مهام محدودة» تفتح تكليفاً أو نقلاً. التوصية الفردية بلا أثر وظيفي | TBH-A، SEP، MOV |
| D-TBH-2 | تشكيل اللجنة الطبية ونصابها ومن يصدر قرارها، ومرجعها القانوني | ثلاثة أعضاء على الأقل مسجّلون مع رئيس، و`hr-health-clinical` حصراً، واعتماد مدير الصحة المهنية | TBH-A |
| D-TBH-3 | التجاوز الطارئ للموافقة | لتوصيات اللياقة العاجلة فقط، بسبب إلزامي ومراجعة ثانية خلال 72 ساعة. ممنوع لقرارات التقاعد والإنهاء | TBH-A |
| D-TBH-4 | التشفير الحقلي للبيانات السريرية (تدّعيه DPIA) | تشفير الحقول النصية السريرية بمفاتيح SOV-003. الأحداث بين الخدمات بلا نص سريري | TBH-B |
| D-TBH-5 | سلّم التقييم وأثره القانوني على العلاوة والترفيع (القانون 50/2004) | ربطهما بنص مصدّق. حتى ذلك لا أثر مالي آلي، ويُعرض آخر تقييم نهائي في طلب الترفيع كتنبيه | TBH-F، MOV، PAY |
| D-TBH-6 | نطاق دورة الأداء في v1، بعد استشارة ممثلي العاملين وفق TRN-002 | تقييم الرئيس المباشر، ثم اعتماد، ثم إقرار الموظف في ESS، ثم تظلم بلجنة. المعايرة و360 وPIP لاحقاً | TBH-F |
| D-TBH-7 | النسب والسقوف والأجر الخاضع والاختصاص (SYD-005) | خطة منافع مؤرخة ومعتمدة هي مصدر الرواتب الوحيد. لا قيم افتراضية في الإنتاج. لا استقطاع دون خطة نافذة لنظام الموظف | TBH-G، PAY |
| D-TBH-8 | علاوة التدريب وبدل البعثة: هل لهما سند؟ | تعطيلهما في الإنتاج حتى يثبت السند. وعند التفعيل يُشترط قرار إداري معتمد لكل صرف | TBH-C، TBH-D، PAY |
| D-TBH-9 | تعهّد الخدمة بعد البعثة أو الدورة | قاعدة لكل بعثة بمرجع قانوني: مدة الالتزام، والاسترداد عند الإخلال عبر الرواتب أو الفصل | TBH-K، SEP |
| D-TBH-10 | علامة النجاح والتحقق العام من الشهادات | علامة نجاح لكل دورة. تحقق عام برمز غير قابل للتخمين، دون معرّف الموظف | TBH-C |
| D-TBH-11 | دور معتمد تدريب منفصل | إضافة `hr-training-approver` وشخصية قبول ثانية | TBH-C |

---

## 6. خارطة التنفيذ المقترحة

```text
الموجة 1 (P0)   TBH-A (القرار الطبي) ║ TBH-B (خصوصية الصحة) ║ TBH-C (مسار التدريب المحكوم)
الموجة 2 (P1)   TBH-D (ESS والتسليمات المالية) ║ TBH-E (سلامة التقييم) ║ TBH-G (الاشتراكات، مع PAY) ║ TBH-H (المواهب)
الموجة 3 (P2)   TBH-F (دورة الأداء، بقرار) ║ TBH-I (التقاعد والمعاش) ║ TBH-J (عمليات الصحة) ║ TBH-K (قدرات التدريب)
مستمر           TBH-V (القبول الحي + التوثيق) — يُغلق مع كل خطة
```

| الخطة | العنوان | الفجوات | الأولوية | يعتمد على | التقدير (يوم عمل) |
|-------|---------|---------|----------|-----------|-------------------|
| TBH-A | حوكمة القرار الطبي وأثره الوظيفي، ومسار الموافقة | G-TBH-01، G-TBH-02 | P0 | D-TBH-1..3 | 8–10 |
| TBH-B | خصوصية البيانات الصحية والوصول وسلامة السجلات | G-TBH-03، G-TBH-10 | P0/P1 | D-TBH-4 (للتشفير فقط) | 7–9 |
| TBH-C | مسار التدريب المحكوم وسلامة السجلات والشهادات | G-TBH-04، G-TBH-05، G-TBH-07 | P0/P1 | D-TBH-8، D-TBH-10، D-TBH-11 | 8–10 |
| TBH-D | التعلم الذاتي ESS وتسليمات التدريب إلى الرواتب | G-TBH-06 | P1 | TBH-C | 4–5 |
| TBH-E | سلامة سجلات التقييم ونطاقها | G-TBH-08، G-TBH-18 | P1 | — | 4–6 |
| TBH-F | دورة تقييم v1 والتظلم والربط بالترفيع | G-TBH-13 | P2 | TBH-E، D-TBH-5/6 | 8–10 |
| TBH-G | ربط الاشتراكات بالرواتب وملف التأمينات | G-TBH-09 | P1 | D-TBH-7، خطة PAY | 8–10 |
| TBH-H | وصول بيانات المواهب والتعاقب وربطها بالملاك | G-TBH-11، G-TBH-17 | P1/P2 | TBH-E | 4–6 |
| TBH-I | تسليمات التقاعد والمعاش والتأمين الصحي | G-TBH-15 | P2 | TBH-A، خطة SEP | 6–8 |
| TBH-J | عمليات الصحة المهنية وربط الإجازة المرضية | G-TBH-16 | P2 | TBH-B، خطة LVA | 5–7 |
| TBH-K | قدرات التدريب: الميزانية والحضور والتعهد | G-TBH-14 | P2 | TBH-C، D-TBH-9 | 7–9 |
| TBH-V | التحقق والتوثيق | G-TBH-12 | P1 ← مستمر | كل الخطط | 5–7 |
| **المجموع** | | | | | **≈ 74–97** |

**TBH-A — حوكمة القرار الطبي.** تفصل القرار الطبي عن الأثر الوظيفي، وتجعل الموافقة قابلة للتشغيل.
- نموذج موافقة وسجل موافقات. `Edit` بنموذج عرض لا يمس الموافقة. تجاوز طارئ بسبب.
- حذف تغيير الحالة من مستهلكَي HR.Core الطبيين. قرار اللجنة يفتح طلباً في HR.Contracts. «مهام محدودة» لا تنشئ `SeparationRecord`.
- `HrHealthClinical` على اللجنة، مع أعضاء ونصاب واعتماد. قفل التقرير بعد النشر وتصحيح بإجراء صريح.
- التعويض بقرار معتمد ومبلغ صريح.
- سكربت يكشف الموظفين الذين حُوّلوا إلى `Retired`/`Active` بحدث طبي، لمراجعة HR.
- القبول: AC-TBH-Aa..Ad.

**TBH-B — خصوصية البيانات الصحية.** تُطبّق مبدأ أقل وصول ونطاق الفرع على الخدمة كلها.
- سياسات صريحة لكل متحكم، ونطاق فرع على كل قائمة وتفاصيل وإجراء.
- مستهلك `TransferExecuted`.
- أحداث بلا نص سريري، وتنظيف النص المنسوخ في HR.Core وHR.Contracts والأرشيف.
- إلغاء ناعم بدل الحذف، ومنع تغيير صاحب السجل، وقفل البطاقة بعد التفعيل.
- تشفير حقلي بعد D-TBH-4.
- القبول: AC-TBH-Ba..Be.

**TBH-C — مسار التدريب المحكوم.** تجعل الترشيح المعتمد هو الطريق الطبيعي إلى الشهادة.
- خدمة تسجيل موحّدة تنشئ الـchecklist وتمنع التكرار.
- `hr-training-approver` وفصل المهام على الترشيح والخطة والبعثة، وهوية المعتمد من المطالبات (claims).
- علامة نجاح وحالة `Failed`.
- رقم شهادة عشوائي فريد، وتحقق بأقل بيانات، وإبطال بدل حذف، وأرشفة ناعمة.
- القبول: AC-TBH-Ca..Ce.

**TBH-D — ESS والتسليمات المالية.** تفتح التعلم الذاتي لجمهوره وتغلق باب التكرار المالي.
- ربط `/ui/hr-training/Ess*` بـ`ModuleEss` (بالتنسيق مع IAM)، ورابط من بوابة الخدمة الذاتية.
- الإتمام من تقدّم الدروس على الخادم، ونشر الحدث مرة واحدة.
- فحص العلامة وفهرس فريد في معالجَي علاوة التدريب وبدل البعثة (مع PAY).
- القبول: AC-TBH-Da، AC-TBH-Db.

**TBH-E — سلامة التقييم.** تثبّت التقييم النهائي وتقيّد الوصول إليه.
- نطاق على `Index/Edit/Delete`، وآلة حالات، والمقيِّم من المطالبات (claims).
- قيد فريد لكل موظف وسنة، وحذف ناعم.
- إصلاح الرسائل المشوّهة الترميز.
- القبول: AC-TBH-Ea، AC-TBH-Eb.

**TBH-F — دورة الأداء v1.** تنفّذ الدورة التي يحددها D-TBH-6.
- `PerformanceCycle` في HR.Core (لا SharedKernel)، وتقييم الرئيس ضمن نطاقه، واعتماد.
- إقرار وتظلم في ESS، ولجنة تظلم.
- عرض آخر تقييم نهائي في طلب الترفيع.
- القبول: AC-TBH-Fa، AC-TBH-Fb.

**TBH-G — الاشتراكات.** تجعل خطة المنافع المصدر الوحيد لاستقطاع الرواتب وملف التأمينات.
- `BenefitPlan` مؤرخة ومعتمدة بنسب وسقف وأجر خاضع ونظام خدمة، وتقرؤها قاعدة الاستقطاع.
- حذف الثوابت، وبذرة الإنتاج بلا قيم.
- ملف تأمينات بمعرّف وطني أو رقم تأمين، وتصنيف المكوّنات برمز.
- فصل التصدير عن الصرف وجعله POST، وتقرير مصالحة.
- القبول: AC-TBH-Ga، AC-TBH-Gb.

**TBH-H — المواهب والتعاقب.** تقيّد بيانات المواهب وتربطها بالملاك.
- سياسة `HrCoreTalent`، ونطاق فرع، وإزالة 11 إجراءً من قائمة الاستثناء.
- الجاهزية من التقييم النهائي وسجل الخدمة.
- ربط المنصب الحرج بالمنصب الممول، وفصل منشئ خطة القوى العاملة عن معتمدها.
- القبول: AC-TBH-Ha..Hc.

**TBH-I — التقاعد والمعاش.** تسلّم التقاعد إلى مسار الفصل وتحفظ تغطية المتقاعد.
- اشتراك في الخطط ومعالون منظَّمون.
- مهمة رصد بلوغ السن تفتح طلب تقاعد (→ SEP).
- ملف إحالة معاش، وإبقاء تغطية المتقاعد، وربط البطاقة بفرع الهويات.
- القبول: AC-TBH-Ia، AC-TBH-Ib.

**TBH-J — عمليات الصحة المهنية.** تضيف الحملة السنوية وتربط الصحة بالإجازة وإصابة العمل.
- حملة الفحص السنوي وقائمة المتأخرين.
- تقرير طبي للإجازة المرضية فوق الحد، وإحالة إلى اللجنة (→ LVA).
- إخطار إصابة العمل، ومهمة انتهاء البطاقات.
- القبول: AC-TBH-Ja، AC-TBH-Jb.

**TBH-K — قدرات التدريب.** تضيف ما ينقص التدريب ليصبح وظيفة إدارية كاملة.
- كلفة وميزانية ومصالحة.
- حضور يومي.
- تعهّد خدمة واسترداده (→ SEP/PAY).
- ربط رمز التأهيل بدليل الوظائف، وطرف تدريب في saga الفصل.
- القبول: AC-TBH-Ka، AC-TBH-Kb.

**TBH-V — التحقق والتوثيق.** تُغلق مع كل خطة.
- `AcceptanceGate` و`[RequiredAcceptance]` لكل سيناريو `AC-TBH-*`.
- شخصيات: `a-health` و`a-clinician` من فرعين، و`a-training` و`a-training-approver`.
- إضافة `HR.Health.Web` و`HR.Training.Web` إلى حارسَي ربط الكيانات وسياسة POST.
- تصحيح HCM-003/004/005 وFUN-004 وDPIA لتطابق الشيفرة، وحذف نماذج SharedKernel غير المستخدمة.
- رحلة E2E على الملف الشبيه بالإنتاج.
- القبول: AC-TBH-Va.

**ملاحظة على التقدير:** الأرقام تقريبية لمطوّر يعرف الشيفرة، وتشمل الاختبارات والتوثيق. أكبر مصادر عدم اليقين:
- القرارات D-TBH-1 وD-TBH-5 وD-TBH-7.
- حجم البيانات السريرية المنسوخة فعلاً في الخدمات الأخرى (TBH-B).

---

## 7. معيار «اكتمال الأداء والتدريب والمنافع والصحة المهنية» المقترح

- [ ] **1. تُغلق كل فجوات P0 وP1** (G-TBH-01…G-TBH-12)، أو يُؤجَّل بعضها بقرار موثّق.
- [ ] **2. تنجح رحلتا E2E بإعدادات Production** (`IsLabScale=false`):
  - **(أ) الصحة:**
    1. الطبيب يسجّل موافقة من الواجهة.
    2. لجنة من ثلاثة أعضاء تقرر «تقاعد صحي».
    3. يُفتح طلب إنهاء معلّق في HR.Contracts، وحالة HR.Core `Active`.
    4. يُعتمد الطلب ويتم، فتصبح الحالة `Retired` مع نشر الحدث.
    5. لا يوجد نص سريري في HR.Core ولا HR.Contracts ولا الأرشيف.
    6. `a-health` يُرفض بـ403 على التقرير.
  - **(ب) التدريب:**
    1. ترشيح من `a-training`.
    2. اعتماد من شخصية ثانية.
    3. إتمام بدرجة ناجحة.
    4. شهادة يتحقق منها متحقق خارجي دون معرّف الموظف.
    5. مؤهل في HR.Core، وعلاوة واحدة على الأكثر.
    6. درجة راسبة لا تنتج شهادة.
- [ ] **3. استقطاع الاشتراكات وملف التأمينات من خطة معتمدة:** تشغيل رواتب يستقطع الاشتراك من خطة منافع نافذة بسقفها. وملف التأمينات يحمل معرّفاً وطنياً، ويطابق مجموع الاستقطاع سطراً سطراً. وتصديره لا يمس ملف الصرف.
- [ ] **4. التقييم مثبّت ومقيّد:** التقييم النهائي غير قابل للتعديل أو الحذف. قائمة التقييمات وبيانات التعاقب مقيّدة بالنطاق والدور. الموظف يرى تقييمه ويقرّ به في ESS.
- [ ] **5. الأدلة والتوثيق:**
  - يوجد مجلد `acceptance-evidence/AC-TBH-*` لكل سيناريو موجب وسالب.
  - حارسا المعمارية يغطيان HR.Health وHR.Training.
  - وثائق HCM-003/004/005 وFUN-004 وDPIA تصف الشيفرة الفعلية، ولا «Done» دون دليل سلوك.
