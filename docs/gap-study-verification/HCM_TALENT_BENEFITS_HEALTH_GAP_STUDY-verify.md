# تحقق مستقل — HCM_TALENT_BENEFITS_HEALTH_GAP_STUDY

**النطاق:** كل فجوات P0 (G-TBH-01..04) وأول ثلاث فجوات P1 (G-TBH-05..07). قراءة ثابتة لـ `/home/claude/hrm` @ `f9eab1a`.

| الفجوة | الأولوية | الحكم | ملاحظة |
|---|---|---|---|
| G-TBH-01 | P0 | مؤكد | لا `RecordConsent` ولا `emergencyOverride` في أي `.cshtml` أو `.js` في المستودع. الكاتبون الوحيدون لـ `ConsentGivenAt`: `Edit` (:146) و`RecordConsent` (:167) والبذرة. و`Edit.cshtml` بلا حقول الموافقة، فكل حفظ يصفّرها. |
| G-TBH-02 | P0 | مؤكد | `Retirement` → `Retired`، و`FitnessForDuty` → `Active`، والانتقالات `Suspended/Seconded→Active` مسموحة، و`Retired` نهائية. HR.Contracts يستهلك `IMedicalDecisionIssued` بلا تمييز لـ `MedicalRef`، فـ`LimitedDuties` يفتح انفصالاً. `MedicalCommitteeReportsController` بلا `[Authorize]`، ولا قاعدة بوابة خاصة بـ hr-health. و`Details = string.Empty`. بند اختلاف الحالة `Retired→Terminated` موسوم «يحتاج تحقق»، والقراءة تؤيده. |
| G-TBH-03 | P0 | مؤكد | كل الأسطر صحيحة: `Notes = model.Findings` (:90)، وسبب السجل في واجهة HR.Core وتصديرها، وعنوان الأرشيف (:50). لا محوّل تشفير في `HealthDbContext` (يوجد `PiiFieldEncryptionService` في ServiceDefaults ولا تستعمله الخدمة). ولا مستهلك `ITransferExecuted` في HR.Health. |
| G-TBH-04 | P0 | مؤكد | `Approve` يضيف `TrainingEnrollment` بلا checklist، والمنشئ الوحيد للـ checklist هو `TrainingEnrollments.Create`. `PreCompleteGate` يرفض عند غياب خطوة `Enrolled`، والمسار الآخر الوحيد لـ `Completed` هو ESS للتعلم الإلكتروني (كيان مختلف). |
| G-TBH-05 | P1 | مؤكد | لا `[Authorize(Policy)]` إلا على `EssLearning`، و`Certificates` عليه `[Authorize]` عام. لا قاعدة بوابة خاصة بـ hr-training. `ApprovedBy = Guid.Empty`، و`NominatedBy` مربوط من النموذج. ومعالج البعثة بلا فحص علامة. |
| G-TBH-06 | P1 | مؤكد | `TryGetEssPolicy` لا يشمل `/ui/hr-training`، فتُطبَّق `ModuleHrTraining`. `UpdateProgress` ينشر عند كل 100% ثم يحفظ (:139)، فالرسالة تُرسل فعلاً، ومعالج العلاوة يحسب `marker` ولا يفحصه. |
| G-TBH-07 | P1 | مؤكد | لا علامة نجاح، والرقم حتمي من 6 محارف، ولا فهرس على `TrainingEnrollment.CertificateNo` (الفهرس الفريد على `LearningCertificate` فقط). الحذف الصلب يتتالى عبر الطبقتين، ولا معترض حذف ناعم. |

## التعديلات على الدراسة
- لا تعديلات؛ كل الفجوات المفحوصة مؤكدة بأدلتها وأسطرها.

## ملاحظات جديدة
- **سياق لـ G-TBH-02 (لا يغيّر الحكم):** إنشاء `SeparationRecord` من `IMedicalDecisionIssued` يمر بـ`ApplyReferenceAsync`، وهو يرمي في إعداد الإنتاج الحالي لأن `hr-contracts-web` بلا `Integration__HrCoreBaseUrl` ولا نطاق `hr-core.workflow` (مغطى في COD_ORG وFUN_002). لذلك لا يظهر شق «فتح ملف إنهاء» قبل إصلاح الإعداد. أما شق تغيير الحالة في HR.Core فلا يمر بالترميز، وهو قائم الآن.
