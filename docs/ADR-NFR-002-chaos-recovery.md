# ADR-NFR-002 — اختبارات الفشل والفوضى والتعافي

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | NFR-002 (البطاقة 40) |
| التاريخ | 2026-07-28 |

## السياق

OPS-004 وOBS-002 جاهزان، وINT-001/002 يغطيان outbox/inbox محاكاةً، لكن بلا harness حقن شبكة، بلا jitter على retries، بلا circuit breaker HTTP ظاهر، وبلا جدول game days يربط الأعطال بالتنبيهات والـrunbooks.

## القرار

1. **Toxiproxy** في [`docker-compose.chaos.yml`](../docker-compose.chaos.yml) (profile `chaos`) لحقن latency/timeout/down/packet-loss أمام PostgreSQL وRabbitMQ ومسار HTTP اختياري.
2. **كتالوج أعطال** إلزامي في [NFR_CHAOS_CATALOG.md](./NFR_CHAOS_CATALOG.md) — كل صف: inject → invariant → alert/runbook.
3. **مرونة ضيقة في الكود:** MassTransit retry بـ**exponential + jitter**؛ **circuit breaker** على عميل `gateway-health-probe` يفشل ظاهراً (BrokenCircuit / unhealthy) دون ابتلاع الخطأ.
4. **نتائج:** `chaos/results/<stamp>-<scenario>/`.
5. **Game days:** جدول ربع سنوي يلفّ drills OPS-004 وحقن Toxiproxy وتنبيهات OBS-002.
6. **خارج النطاق:** مغلف CAP-001؛ ransomware OPS-006؛ مسرح حوادث SRE-003 الكامل؛ Chaos Mesh كشرط إغلاق.

## العواقب

- NAT-006 يستهلك الكتالوج والنتائج.
- تحديث runbooks من نتائج game day إلزامي لإغلاق البطاقة.
