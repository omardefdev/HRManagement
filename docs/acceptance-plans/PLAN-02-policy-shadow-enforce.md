# PLAN-02 — السياسات الحية Shadow/Enforce (AC-09, AC-10)

**التغطية:** AC-09, AC-10. **الطبقة:** 2. **يعتمد على:** PLAN-00.

## الهدف

إثبات أن إصداراً جديداً من السياسة يعمل في وضع Shadow دون تغيير القرار الفعلي، وأن التحويل إلى Enforce يطبّق الإصدار الجديد فقط مع حفظ الإصدار السابق ودفتر الإنفاذ.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: كيانات ومسارات المحرك في [PolicyEntities.cs](../../src/Services/HRCore/HR.Core.Domain/Entities/Policy/PolicyEntities.cs) و[PolicyOrchestrator.cs](../../src/Services/HRCore/HR.Core.Infrastructure/Services/Policy/PolicyOrchestrator.cs)؛ نقاط النهاية `simulate/publish/enforce/evaluate/ledger` في `PoliciesController`؛ عميل [PolicyEnforceClient.cs](../../src/Shared/HRManagement.ServiceDefaults/Policy/PolicyEnforceClient.cs). Shadow يكتب `PolicyEnforcementLedger` ويُبقي القرار الفعلي `true`. النشر يتطلب أربع عيون؛ التقييم يحمّل أحدث إصدار منشور ضمن نافذة النفاذ.
- فجوة: لا يوجد بديل تكامل موحّد لـ`IPolicyEnforceClient` (مذكور في §3.17 وCAP-POL-01)؛ لا إغلاق تلقائي لـ`EffectiveTo` للإصدار السابق عند Enforce؛ يلزم التحقق من إبطال الكاش عند النشر.

## تغييرات التنفيذ (مهام)

1. إضافة `FakePolicyEnforceClient` مشترك في مشروع الاختبارات لحقن قرارات محددة في التكامل.
2. عند Enforce لإصدار جديد: إغلاق `EffectiveTo` للإصدار السابق تلقائياً وضمان انتقاء الإصدار الفعّال الصحيح في `PolicyOrchestrator`.
3. التحقق من إبطال إدخالات `MemoryCache` عند النشر (نمط المفتاح الحالي قد لا يُنظّف؛ إصلاح إن لزم).
4. سكربت HTTP على `/api/policies/*` ينفّذ: إنشاء إصدار ← simulate/Shadow ← enforce ← قراءة الدفتر.

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-09 | إصدار جديد + Shadow ⇒ `PolicySimulationRun` وسجل Shadow في الدفتر | القرار الفعلي للمعاملة لا يتغير في Shadow |
| AC-10 | Enforce ⇒ الإصدار الجديد فقط يُطبَّق، والسابق محفوظ مع `EffectiveTo`، والدفتر يسجّل الإنفاذ | لا يُطبَّق إصداران معاً؛ ولا يُعدَّل إصدار منشور (يُنشأ إصدار جديد) |

## منهج الاختبار

مجموعة `Ac09..Ac10` عبر البوابة على HR.Core الحي؛ إعادة استخدام [PolicyEngineTests](../../src/Tests/HRManagement.UnitTests/) و[docs/POLICY_ENGINE_UAT.md](../POLICY_ENGINE_UAT.md).

## مخرجات الأدلة

`acceptance-evidence/AC-09..AC-10/`: طلب/استجابة النشر والإنفاذ، `db-checks.json` لصفوف `PolicyPublication`/`PolicyEnforcementLedger` وإصدارات النفاذ، ولقطة قبل/بعد للقرار.

## تعريف الإنجاز

Shadow لا يغيّر القرار؛ Enforce يطبّق الجديد فقط ويحفظ السابق والدفتر؛ AC-09/AC-10 = `Pass`.
