# NFR-001 — طوبولوجيا اختبار الحمل

القرار: [ADR-NFR-001-capacity-load.md](./ADR-NFR-001-capacity-load.md). النموذج: [NFR_CAPACITY_MODEL.md](./NFR_CAPACITY_MODEL.md).

## المكوّنات

```text
k6 (Docker grafana/k6 | محلي)
  → Gateway BASE_URL (+ ACCESS_TOKEN)
  → HR.Core / Payroll / Files عبر YARP
Prometheus + Grafana (OBS-001/002)  ← مقاييس أثناء التشغيل
perf/results/<run-id>/              ← ملخص JSON + stdout
```

## متغيرات البيئة

| متغير | مثال | الغرض |
|---|---|---|
| `BASE_URL` | `http://localhost:5000` | بوابة |
| `ACCESS_TOKEN` | JWT | مسارات مصادقة |
| `NFR_PROFILE` | `smoke` / `load` / `soak` / `spike` / `stress` | شكل الحمل |
| `NFR_SCALE_FACTOR` | `0.002` | توثيق فقط في الملخص |
| `PAYROLL_RUN_ID` | GUID | سيناريو الرواتب |
| `PROMETHEUS_URL` | `http://localhost:9090` | جمع اختياري أثناء التشغيل |

## مسارات المخرجات

```text
perf/results/
  <yyyyMMdd-HHmmss>-<profile>/
    summary.json          # k6 handleSummary
    meta.json             # env + git sha + scale
    notes.md              # ملاحظات المشغّل
```

## تشغيل سريع

```powershell
# seed (افتراضي 10k)
pwsh scripts/nfr-seed-synthetic.ps1 -Employees 10000 -OutDir data/nfr-seed

# smoke
pwsh scripts/nfr-k6-run.ps1 -Scenario smoke -Profile smoke

# load بوابة
pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile load
```

أو:

```bash
bash scripts/nfr-k6-run.sh gateway-mixed load
```

Compose اختياري: [`docker-compose.load.yml`](../docker-compose.load.yml) — خدمة k6 لمرة واحدة، ليست جزءاً من إنتاج.

## Staging

1. `scripts/verify-staging-stack.ps1` (صحة).
2. Seed ضد قواعد staging.
3. تشغيل load/soak مع observability مرفوعة (`docker-compose.observability.yml`).
4. حفظ النتائج تحت `perf/results/` وإرفاقها بتقرير التحقق.
