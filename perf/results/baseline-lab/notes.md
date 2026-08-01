# Baseline lab — NFR-001

منهجية توقيع النتائج المخبرية (قبل تشغيل staging الكامل).

## ما تم تثبيته في المستودع

- نموذج الحمل + ADR + طوبولوجيا
- Seed اصطناعي (`scripts/nfr-seed-synthetic.ps1`) بحجم افتراضي 10k
- سيناريوهات k6 + عتبات J2 + أوضاع load/soak/spike/stress
- أغلفة تشغيل تكتب تحت `perf/results/<stamp>-.../`

## تشغيل موقّع على Staging (مطلوب للأرقام الحية)

1. `verify-staging-stack` + observability.
2. Seed 10k → apply SQL بعد مواءمة الأعمدة.
3. نفّذ smoke → load → soak 2h → spike/stress.
4. انسخ `summary.json` الحقيقي هنا أو إلى مجلد ختم زمني جديد.
5. أرفق لقطات Grafana للذاكرة/الاتصالات/الطوابير في `notes.md`.

## قابلية التكرار

نفس `Seed=42` + نفس `VUS_BASE` + نفس `NFR_PROFILE` يجب أن تقع الأزمنة/المعدلات ضمن ±15%.
