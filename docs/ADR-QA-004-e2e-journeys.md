# ADR-QA-004 — E2E لرحلات الأعمال والصلاحيات

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | QA-004 (البطاقة 43) |
| التاريخ | 2026-07-28 |

## السياق

QA-001..003 ثبّتا بوابات CI والتغطية والبنية التحتية الحقيقية. طبقة Playwright الحالية smoke للبوابة فقط، بلا رحلات أعمال/صلاحيات ولا seed إصداري ولا nightly على صورة الإصدار.

## القرار

1. **إثبات الرحلات = Playwright + API** عبر Gateway؛ QA-003 تبقى دليل البنية التحتية.
2. **Skip / Fail:** `RUN_PLAYWRIGHT_E2E` غير مفعّل → Skipped؛ مفعّل بلا مكدس أو أسرار persona مطلوبة → **Failed**؛ لا Pass صامت.
3. **503/5xx ≠ نجاح** في أي assertion رحلة (`E2eHttp.EnsureNotServerError` / `EnsureSuccessNot5xx`).
4. **مصفوفة** [QA_E2E_JOURNEY_MATRIX.md](./QA_E2E_JOURNEY_MATRIX.md): كل رحلة حرجة ≥1 happy + ≥3 fail/auth.
5. **Seed إصداري** تحت `testdata/e2e/` + personas Keycloak في `docker/keycloak/realm-qa004-e2e.json`.
6. **آثار الفشل:** trace + screenshot تحت `TestResults/e2e-artifacts/`.
7. **إغلاق CI:** nightly + `workflow_dispatch` على صور الإصدار؛ وسم `e2e` على PR يبقى smoke اختياري.
8. **خارج النطاق:** توقيع FUN-001..004؛ SUP-001؛ إعادة QA-003؛ UX-002.

## العواقب

- Suites بـ`Category=Journey` أطول وتتطلب مكدس + personas.
- Gateway smoke يبقى منفصلاً كبوابة رقيقة.
