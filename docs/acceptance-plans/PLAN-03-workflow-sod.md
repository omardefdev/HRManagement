# PLAN-03 — سير العمل + فصل الواجبات (AC-11, AC-12)

**التغطية:** AC-11, AC-12. **الطبقة:** 2. **يعتمد على:** PLAN-00، PLAN-02.

## الهدف

إثبات أن قرار السياسة يبدأ المسار الصحيح متعدد المراحل مع منع تجاوز المراحل وحفظ التوقيعات والقرارات، وأن فصل الواجبات يمنع المنفّذ نفسه من إنشاء الطلب واعتماده وصرفه.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: [WorkflowEngine.cs](../../src/Shared/HRManagement.ServiceDefaults/Workflow/WorkflowEngine.cs)، متحكمات `WorkflowDefinitionsController`/`WorkflowInstancesController`، تثبيت الإصدار على المثيل، والتقدّم عبر `FindNextApplicableStep` (لا قفز عشوائي). `PolicyWorkflowBinding` يربط قرار Enforce ببدء مسار. SoD عبر [SeparationOfDutiesService.cs](../../src/Shared/HRManagement.SharedKernel/SeparationOfDuties/SeparationOfDutiesService.cs) وموانع Payroll في [PayrollRunHandlers.cs](../../src/Services/Payroll/Payroll.Application/Features/PayrollRuns/PayrollRunHandlers.cs) (منشئ ≠ معتمد ≠ مُرحِّل).
- فجوة: الموافقة/الرفض في `WorkflowInstancesController` لا تُنشئ [SignatureRecord](../../src/Shared/HRManagement.SharedKernel/Signatures/SignatureRecord.cs)؛ لا اختبار API صريح لمنع القفز غير الشرعي بين المراحل؛ "الصرف" في AC-12 يحتاج خريطة لأمر الدفع؛ ازدواج مسار SoD (inline في Payroll مقابل الخدمة المركزية).

## تغييرات التنفيذ (مهام)

1. ربط `SignatureRecord` بموافقة/رفض سير العمل (فاعل/وقت/دليل/الإصدار).
2. اختبار API يرفض محاولة الانتقال إلى مرحلة غير المرحلة التالية الصحيحة.
3. خريطة "الصرف" (Pay/PaymentBatch) ضمن قواعد SoD وتوحيدها على `SeparationOfDutiesService`.

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-11 | قرار سياسة يتطلب موافقة ⇒ بدء المسار الصحيح، وحفظ التوقيعات والقرارات لكل مرحلة | محاولة تجاوز مرحلة تُرفض |
| AC-12 | مستخدمون مختلفون ينجزون إنشاء/اعتماد/صرف | المنفّذ نفسه يحاول إنشاء + اعتماد + صرف ⇒ منع بـSoD |

## منهج الاختبار

مجموعة `Ac11..Ac12` عبر البوابة؛ إعادة استخدام [Wf001WorkflowEngineTests](../../src/Tests/HRManagement.UnitTests/)، `SoDForgedApproverTests`، `SoDPolicyIntegrationTests`، [PayrollRunLifecycleIntegrationTests](../../src/Tests/HRManagement.IntegrationTests/).

## مخرجات الأدلة

`acceptance-evidence/AC-11..AC-12/`: تسلسل الطلبات/الاستجابات لكل مرحلة، `db-checks.json` لـ`WorkflowInstance`/`SignatureRecord`/سجل الخطوات، ورسالة الرفض عند التجاوز/الانتهاك.

## تعريف الإنجاز

المسار الصحيح يبدأ، التجاوز ممنوع، التوقيعات محفوظة، وSoD يمنع تركيز الأدوار؛ AC-11/AC-12 = `Pass`.
