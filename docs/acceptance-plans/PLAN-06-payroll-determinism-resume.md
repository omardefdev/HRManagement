# PLAN-06 — حتمية واستئناف الرواتب (AC-21, AC-22)

**التغطية:** AC-21, AC-22. **الطبقة:** 3. **يعتمد على:** PLAN-00.

## الهدف

إثبات أن تشغيل الراتب مرتين على لقطة المدخلات نفسها يُنتج النتيجة نفسها مع بقاء المدخلات والقواعد قابلة للتفسير، وأن الإيقاف في منتصف الأجزاء ثم الاستئناف يستكمل من نقطة التوقف دون أسطر أو مبالغ مكررة.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: لقطة المدخلات [PayrollRunInputSnapshot.cs](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRunInputSnapshot.cs)؛ بصمة القاعدة [PayrollConfigurationHashService.cs](../../src/Services/Payroll/Payroll.Domain/Services/PayrollConfigurationHashService.cs)؛ تثبيت الإصدار على `PayrollRun`؛ التشغيل القابل للاستئناف [PayrollRunJob.cs](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRunJob.cs) (lease/checkpoint) + `PayrollRunPartition`؛ الفهرس الفريد `IX_SalaryLines_Run_Employee` يمنع تكرار السطر.
- فجوة: الإثبات الأقوى على SQLite/محرك لا على PostgreSQL حي متعدد العمّال مع قتل حقيقي في منتصف الأقسام؛ لا واجهة/API تُظهر اللقطة والبصمة للمدقق ("قابلية التفسير").

## تغييرات التنفيذ (مهام)

1. **AC-21:** سكربت يشغّل الدورة مرتين على اللقطة نفسها في PostgreSQL ويقارن `SalaryLines` بت-لبت (بما في ذلك ترتيب المكوّنات تحت العمّال المتوازين)؛ عرض/إرجاع اللقطة + بصمة القاعدة عبر API للمدقق.
2. **AC-22:** قتل عملية/عامل فعلي في منتصف الأقسام ثم الاستئناف؛ إثبات المتابعة من `checkpoint` دون تكرار أسطر/مبالغ، مع منع عاملين نشطين عبر الـlease.

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-21 | تشغيلان على اللقطة نفسها ⇒ نتيجة متطابقة، واللقطة/القاعدة قابلتان للتفسير | لا اختلاف بين التشغيلين |
| AC-22 | إيقاف في منتصف الأقسام ثم استئناف ⇒ إكمال من نقطة التوقف | لا أسطر/مبالغ مكررة؛ لا عامل ثانٍ نشط |

## منهج الاختبار

مجموعة `Ac21..Ac22`؛ إعادة استخدام [Pay002ImmutableConfigTests](../../src/Tests/HRManagement.UnitTests/) (`Replay_SameInputsAndPinnedConfig_BitForBit`) و[Pay003ResumablePayrollRunTests](../../src/Tests/HRManagement.IntegrationTests/) لكن على PostgreSQL حي.

## مخرجات الأدلة

`acceptance-evidence/AC-21/`: مقارنة نتيجتي التشغيل + عرض اللقطة/البصمة. `AC-22/`: حالة الأقسام قبل/بعد الاستئناف + عدّ الأسطر (لا تكرار) + `timing.json`.

## تعريف الإنجاز

النتيجة حتمية وقابلة للتفسير؛ الاستئناف بلا تكرار؛ AC-21/AC-22 = `Pass`. (قياس الأداء/السعة يبقى Deferred — CAP-PAY-02.)
