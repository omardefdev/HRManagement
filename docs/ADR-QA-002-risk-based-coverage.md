# ADR-QA-002 — استراتيجية تغطية مبنية على المخاطر

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | QA-002 (البطاقة 41) |
| التاريخ | 2026-07-28 |

## السياق

QA-001 وفّر Coverlet + دمج Cobertura + عتبة عامة 40%، بلا مصفوفة مخاطر→اختبار، بلا floors لكل assembly، بلا mutation، وبلا سياسة flaky/ownership.

قياس تغطية كل assemblies المضمّنة في `coverlet.runsettings` يبقى منخفضاً بسبب hosts رقيقة وبنى تحتية بلا مسار وحدة (Gateway UI، FileStorage shell، Infrastructures غير محمّلة). استراتيجية مبنية على المخاطر تقيّم البوابة على **مجموعة assemblies حرجة** لا على متوسط يخفي مخاطر الرواتب/النواة خلف ضوضاء غير مستهدفة.

## القرار

1. **مصفوفة** [QA_RISK_TO_TEST_MATRIX.md](./QA_RISK_TO_TEST_MATRIX.md) تربط invariants P0/P1 ومسارات حرجة بطبقات الهرم ومعرّفات اختبار ومالك.
2. **عتبة عامة line coverage = 60%** تُحسب على `gateAssemblies` في [`coverage-floors.json`](../coverage-floors.json) (لا على كل حزمة Coverlet). `COVERAGE_THRESHOLD` يبقى قابلاً للتجاوز المخبري فقط.
3. **أرضيات assemblies** في نفس الملف — لـ`Payroll.*` / `HR.Core.*` / Gateway / ServiceDefaults / `Compliance.*` وغيرها المُدرجة. تُرفع بـratchet ولا تُخفَّض في CI دون تحديث هذا الـADR.
4. **Stryker.NET** على `Payroll.Domain` (`tests/stryker-payroll.json`)؛ workflow مجدول/`workflow_dispatch`؛ عتبة mutation ابتدائية **60%** على النطاق المحدود.
5. **استثناءات Coverlet** موثّقة في [QA_COVERAGE_EXCLUSIONS.md](./QA_COVERAGE_EXCLUSIONS.md)؛ Migrations المولَّدة تبقى مستثناة.
6. **Flaky policy** في [QA_FLAKY_TEST_POLICY.md](./QA_FLAKY_TEST_POLICY.md) — لا إعادة لا نهائية؛ quarantine صريح.
7. **خارج النطاق:** Testcontainers كدليل وحيد (QA-003)؛ E2E رحلات كاملة (QA-004)؛ SBOM (SUP-001).

## العواقب

- CI يفشل دون 60% على `gateAssemblies` أو دون floor لـassembly ظاهر في التقرير.
- Mutation ليس حاجز كل PR؛ nightly يثبت الدرجة المعتمدة.
- توسيع `gateAssemblies` أو رفع floors مسموح؛ التخفيض يتطلب ADR محدّث.
