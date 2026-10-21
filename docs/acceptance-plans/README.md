# خطط استكمال اختبارات القبول (AC-01..AC-37)

**الغرض:** تجهيز النظام لتشغيل **اختبارات قبول موجبة وسالبة** على بيئة Docker حية قريبة من الإنتاج، وإثبات ادعاءات الأطروحة بالدليل لا بوجود الآلية فقط. هذا المجلد يقسّم العمل إلى **14 خطة تنفيذية مستقلة**، كل خطة قابلة للتشغيل والمتابعة والإثبات وحدها.

> الخطة الأمّ الموحّدة (نظرة عامة واحدة) محفوظة في ملف خطة الجلسة؛ هذه الملفات هي التفصيل القابل للتنفيذ لكل مسار.

## حكم الجاهزية (ملخص)

النظام **ناضج على مستوى الكود** — معظم آليات `AC-01..37` منفّذة ومغطاة باختبارات وحدة/تكامل، ويصنّفها [سجل الحقيقة](../THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md) بأنها "منفذ ولم يكتمل التحقق". **غير جاهز الآن** لتشغيل القبول الحي بسبب: غياب بذور متعددة الجهات وشجرة مركز/فروع، غياب مستخدمي Keycloak للأدوار، غياب إطار قبول يولّد الأدلة، وعدد محدود من فجوات المنتج. **الخطة 0 (الأساس) بوابة إلزامية** لكل ما بعدها.

## الفهرس والطبقات

| الخطة | التغطية | الطبقة | يعتمد على |
|-------|---------|--------|-----------|
| [PLAN-00 — الأساس](PLAN-00-foundation.md) | البيئة + البذور + الشخصيات + إطار الأدلة | بوابة إلزامية | — |
| [PLAN-01 — عزل المركز والفروع](PLAN-01-center-branch-isolation.md) | AC-01..AC-06 | 1 (قصوى) | PLAN-00 |
| [PLAN-02 — السياسات Shadow/Enforce](PLAN-02-policy-shadow-enforce.md) | AC-09, AC-10 | 2 | PLAN-00 |
| [PLAN-03 — سير العمل + فصل الواجبات](PLAN-03-workflow-sod.md) | AC-11, AC-12 | 2 | PLAN-00, PLAN-02 |
| [PLAN-04 — الرموز المتوازية + الهيكل المؤرخ](PLAN-04-codes-temporal-org.md) | AC-13, AC-14 | 2 | PLAN-00 |
| [PLAN-05 — اعتمادية الرسائل](PLAN-05-messaging-reliability.md) | AC-15..AC-18 | 2 | PLAN-00 |
| [PLAN-06 — حتمية واستئناف الرواتب](PLAN-06-payroll-determinism-resume.md) | AC-21, AC-22 | 3 | PLAN-00 |
| [PLAN-07 — الرقم الوطني + Mass Assignment](PLAN-07-national-id-mass-assignment.md) | AC-26, AC-31 | 3 | PLAN-00 |
| [PLAN-08 — التهيئة + الحجر](PLAN-08-onboarding-quarantine.md) | AC-07, AC-08 | 4 | PLAN-00 |
| [PLAN-09 — المصالحة + عزل قواعد البيانات](PLAN-09-reconciliation-db-isolation.md) | AC-19, AC-20 | 4 | PLAN-00 |
| [PLAN-10 — تجميد/صرف جزئي/احتيال](PLAN-10-payroll-money.md) | AC-23, AC-24, AC-25 | 4 | PLAN-00, PLAN-06 |
| [PLAN-11 — DSAR + الملفات + ثقة الخدمات](PLAN-11-file-dsar-s2s.md) | AC-27..AC-30 | 4 | PLAN-00 |
| [PLAN-12 — عهدة اللوجستيات](PLAN-12-logistics-custody.md) | AC-32, AC-33 | 4 | PLAN-00, PLAN-01 |
| [PLAN-13 — النسخ + الأداء + PDF + التدقيق](PLAN-13-ops-quality.md) | AC-34..AC-37 | 4 | PLAN-00 |

**ترتيب التنفيذ الموصى به:** PLAN-00 ← PLAN-01 ← (PLAN-02..05) ← (PLAN-06, PLAN-07) ← (PLAN-08..13). الطبقات تتبع أولوية الأطروحة: القصوى AC-01..06، ثم AC-09..18، ثم AC-21/22/26/31، ثم الباقي.

## البيئة الموحّدة للتشغيل

```powershell
# رفع الحزمة الكاملة (بنية تحتية + 14 خدمة + بوابة)
pwsh scripts/acceptance-up.ps1   # يُنشأ في PLAN-00؛ يغلّف الأوامر أدناه
# تشغيل كل سيناريوهات القبول AC-01..37 + تجميع الأدلة:
pwsh scripts/acceptance-run-all.ps1
# أو يدوياً:
docker compose -f docker-compose.yml -f docker-compose.services.yml -f docker-compose.local.yml `
  -f docker-compose.dev-profiles.yml -f docker-compose.full-stack-gateway.yml --profile full-stack up -d --wait
pwsh scripts/verify-staging-stack.ps1
```

- نقطة الدخول الوحيدة: البوابة `http://localhost:5000`.
- تكرار في CI عبر توسعة وظيفة E2E الإلزامية في [.github/workflows/ci.yml](../../.github/workflows/ci.yml) و[.github/workflows/e2e-journeys.yml](../../.github/workflows/e2e-journeys.yml).

## عقد الأدلة لكل سيناريو (موحّد)

يكتب مُسجِّل الأدلة (`EvidenceRecorder`، يُنشأ في PLAN-00) لكل سيناريو مجلداً `acceptance-evidence/AC-XX/` يحوي العناصر الثمانية المطلوبة:

| الملف | المحتوى |
|-------|---------|
| `metadata.json` | البيئة + التاريخ + إصدار النظام (git sha + وسم الصور) |
| `steps.md` | البيانات المسبقة والخطوات الدقيقة |
| `http/*.json` | الطلب والاستجابة وحالة HTTP لكل خطوة |
| `before.json` / `after.json` | لقطة قبل وبعد التنفيذ (API أو DB) |
| `audit.json` | سجل التدقيق + `CorrelationId` |
| `db-checks.json` | فحص DB أو Outbox/Inbox/RLS عند الحاجة |
| `timing.json` | الزمن المقاس + النتيجة الفعلية |
| `result.json` | `Pass` / `Fail` / `Blocked` + سبب الفشل |

تجميع نهائي: `acceptance-evidence/summary.md` لكل الـ37، وربطه بمصفوفة تتبع الأطروحة.

## تعريف الإنجاز (DoD) لكل خطة

1. كل تغييرات المنتج/الربط منفّذة ومبنية بلا أخطاء، والاختبارات القائمة ذات الصلة خضراء.
2. كل `AC` في الخطة له سيناريو **موجب** وسيناريو **سالب** يُشغَّل على الحزمة الحية.
3. مجلد أدلة كامل لكل `AC` وفق العقد أعلاه، ونتيجة `Pass` (أو `Blocked` بسبب موثّق للعناصر المؤجلة).
4. تحديث [سجل الحقيقة](../THESIS_CAPABILITY_TRUTH_REGISTER_CURRENT.md) للقدرات المعنية من "منفذ ولم يكتمل التحقق" إلى "منفذ ومتحقق".

## عناصر مؤجلة صراحةً (Deferred — تُثبت الآلية لا التكامل الخارجي)

قناة مصرف/خزينة حية (AC-24/25)، اعتماد رواتب قانوني، CDR/ClamAV مؤسسي مصدّق (AC-28)، مفاتيح إنتاج HSM (AC-26)، قبول ميداني، وأهداف أداء رقمية مضمونة (AC-35). تُوسم في `summary.md` كـ `Deferred` بسبب موثّق، دون تصنيفها فشلاً (تماشياً مع §3.15 من الأطروحة).
