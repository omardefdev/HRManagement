# NFR-001 — تحقق السعة والحمل

القرار: [ADR-NFR-001-capacity-load.md](./ADR-NFR-001-capacity-load.md). النموذج: [NFR_CAPACITY_MODEL.md](./NFR_CAPACITY_MODEL.md).

## أوامر إعادة التشغيل

```powershell
# 1) Seed 10k (SQL + manifest)
pwsh scripts/nfr-seed-synthetic.ps1 -Employees 10000

# 2) Smoke (محلي أو Docker)
pwsh scripts/nfr-k6-run.ps1 -Scenario smoke -Profile smoke -Docker

# 3) Load بوابة / lookup
$env:ACCESS_TOKEN = "<jwt>"
pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile load -Docker
pwsh scripts/nfr-k6-run.ps1 -Scenario lookup-list -Profile load -Docker

# 4) Payroll 10k (بعد إنشاء run على بيانات seed)
$env:PAYROLL_RUN_ID = "<guid>"
pwsh scripts/nfr-k6-run.ps1 -Scenario payroll-run -Profile load -Docker

# 5) Soak مخبري ≥2h (staging)
$env:SOAK_DURATION = "2h"
pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile soak -Docker

# 6) Spike / stress
pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile spike -Docker
pwsh scripts/nfr-k6-run.ps1 -Scenario gateway-mixed -Profile stress -Docker
```

Compose:

```bash
docker compose -f docker-compose.load.yml --profile load run --rm k6-smoke
```

## معايير النجاح / الفشل

| فحص | نجاح |
|---|---|
| smoke | خروج 0؛ ready يُستجاب |
| load gateway/lookup | `http_req_failed` &lt; 1% و p(95) &lt; 2000ms (مع توكن صالح ومسارات 200) |
| payroll 10k | اكتمال الحساب؛ fail ratio &lt; 1% (J4) من مقاييس/سجلات |
| soak ≥2h | لا نمو خطي واضح لـRSS / DB connections / queue على Grafana |
| stress | أخطاء أعلى مسموحة إن وُثّق degradation (429 / طوابير) دون فساد بيانات |

> ملاحظة: بدون `ACCESS_TOKEN` قد تكون الاستجابات 401 — عندها عتبات المدة ما زالت تُقاس؛ اعتبر التشغيل «عقد harness» لا إثبات SLO كامل حتى توفر هوية اختبار.

## جمع Prometheus أثناء التشغيل

```text
http_server_request_duration_seconds_*
probe_success
outbox_pending_messages / outbox_oldest_age_seconds
process_resident_memory_bytes / pg connections / rabbit queue depth
hrm.payroll.run employees_failed / employees_processed
```

احفظ لقطات أو استعلامات في `perf/results/<run>/notes.md`.

## نتائج خام قابلة للمقارنة

قالب مختبر موقّع منهجياً: [`perf/results/baseline-lab/`](../perf/results/baseline-lab/).  
كل تشغيل جديد يضيف مجلداً مختوماً بالوقت عبر `nfr-k6-run`.

## امتداد CAP-001

نفس السيناريوهات والـseed مع `scale_factor→1` و soak 72h / stress 150% / spike 200% — خارج إغلاق NFR-001.
