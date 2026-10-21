# PLAN-10 — تجميد الدورة + الصرف الجزئي + حجز الاحتيال (AC-23, AC-24, AC-25)

**التغطية:** AC-23, AC-24, AC-25. **الطبقة:** 4. **يعتمد على:** PLAN-00، PLAN-06.

## الهدف

إثبات أن تعديل بيانات الموظف بعد تثبيت الدورة لا يغيّرها (يؤثر بدورة لاحقة فقط)، وأن الصرف الجزئي يُنشئ المبلغ المصروف والمتبقي والمتأخرات وأمر الدفع باتساق، وأن اكتشاف اشتباه قبل الصرف يضع Fraud Hold ويمنع الدفع حتى قرار مستخدم مخوّل مختلف.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ (AC-23): `PayrollRun.ResultsFrozen` + `ValidateCanModifyLines` في [PayrollRun.cs](../../src/Services/Payroll/Payroll.Domain/Entities/PayrollRun.cs)؛ التصحيح يرث تثبيت القاعدة.
- منفّذ (AC-24): [PartialDisbursementService.cs](../../src/Services/Payroll/Payroll.Application/Services/PartialDisbursementService.cs) بمتباينة (مصروف+متأخر=استحقاق)؛ `FundingDecision`/`DisbursementPriorityRule`/`ArrearsLedgerEntry`؛ منسّق الأوامر `PaymentOrderCoordinator`. اختبارات `Pay004ArrearsTests`.
- منفّذ (AC-25): [FraudAnomalyEngine.cs](../../src/Services/Payroll/Payroll.Application/) ⇒ `FraudAlert`+`PaymentHold`؛ بوابة [PaymentDisbursementGate.cs](../../src/Services/Payroll/Payroll.Application/); فك بأربع عيون [PaymentHoldService.cs](../../src/Services/Payroll/Payroll.Application/Services/PaymentHoldService.cs). اختبارات `Frd001FraudEngineTests`.
- فجوة: AC-23 يحتاج سيناريو عابر للخدمات حياً (تعديل موظف بعد التجميد)؛ AC-24 `PartialDisbursementService` لا يُنشئ `PaymentOrder` (مسار منفصل) — يلزم رحلة واحدة؛ AC-25 لا مسار HTTP كامل، والبوابة تحجز الدفعة كاملة (تأكيد مطابقته لصياغة "منع الدفع").

## تغييرات التنفيذ (مهام)

1. **AC-23:** سيناريو حي: تجميد دورة A ← تعديل ملف أجر الموظف في HR.Core ← إعادة حساب A ترفض/لا تتغير ← دورة لاحقة B تعكس التعديل.
2. **AC-24:** ربط `PaymentOrder` في رحلة واحدة: FundingDecision ← Allocate ← Arrears ← PaymentOrder، مع تأكيد المتباينة لكل موظف.
3. **AC-25:** مسار HTTP: كشف ⇒ `PaymentHold` ⇒ محاولة صرف ممنوعة ⇒ فك بمستخدم مخوّل مختلف ⇒ صرف مسموح؛ حسم نطاق الحجز (موظف مقابل دفعة).

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-23 | تعديل بعد التجميد يؤثر بدورة لاحقة | الدورة المثبتة لا تتغير |
| AC-24 | صرف جزئي ⇒ مصروف/متبقٍ/متأخرات/أمر دفع متسق | إعادة ملف/رسالة لا تدفع الرصيد مرتين |
| AC-25 | فك الحجز بمستخدم مخوّل مختلف ⇒ يسمح بالدفع | الدفع ممنوع أثناء الحجز؛ ومنشئ التنبيه لا يفكّه وحده |

## منهج الاختبار

مجموعة `Ac23..Ac25`؛ إعادة استخدام `Pay002ImmutableConfigTests`، `Pay004ArrearsTests`، `Frd001FraudEngineTests` على PostgreSQL حي عبر البوابة.

## مخرجات الأدلة

`acceptance-evidence/AC-23/`: نتائج الدورة قبل/بعد التعديل. `AC-24/`: دفتر المتأخرات + أمر الدفع + تأكيد المتباينة. `AC-25/`: محاولة صرف ممنوعة + فك بأربع عيون + صرف بعد الفك.

## تعريف الإنجاز

التجميد يصمد؛ الصرف الجزئي متسق مع أمر دفع؛ حجز الاحتيال يمنع الدفع ويُفَك بفصل واجبات؛ AC-23..25 = `Pass`. (قناة المصرف/الخزينة والاعتماد القانوني تبقى Deferred — CAP-PAY-05/CAP-PAY-LEGAL.)
