# NFR-002 — كتالوج الأعطال

القرار: [ADR-NFR-002-chaos-recovery.md](./ADR-NFR-002-chaos-recovery.md). الطوبولوجيا: [NFR_CHAOS_TOPOLOGY.md](./NFR_CHAOS_TOPOLOGY.md).

| عطل | حقن | ثابت متوقع | تنبيه / Runbook |
|---|---|---|---|
| dependency latency | `nfr-chaos-inject latency pg\|rabbit` | أخطاء ظاهرة؛ outbox يحتفظ؛ لا commit صامت | J2/J3 burn → [OBS_ALERT_RUNBOOKS.md](./OBS_ALERT_RUNBOOKS.md)؛ [NFR_CHAOS_RUNBOOKS.md](./NFR_CHAOS_RUNBOOKS.md) |
| dependency down | `inject down` | فشل صريح؛ استعادة بعد `restore` | page/ticket حسب الرحلة |
| packet loss | `inject loss` | retries محدودة + jitter؛ لا أثر مزدوج نهائي | J3 freshness |
| duplicate events | suite Inbox / سكربت إعادة consume | أثر واحد لكل (tenant, messageId, consumer) | — |
| out-of-order | `Nfr002OutOfOrderTests` | حالة نهائية idempotent / last-write-wins موثّق | — |
| disk full | `nfr-chaos-disk-pressure` على مسار overflow | فشل/spill معلوم لا صمت | audit DLQ health |
| clock skew | JWT خارج `ClockSkew` (±2m) | 401 ظاهر ضمن السياسة | [NFR_CHAOS_RUNBOOKS.md](./NFR_CHAOS_RUNBOOKS.md)#clock-skew |
| expired cert | stub شهادة منتهية / `nfr-chaos-expired-cert` | فشل TLS ظاهر | PRODUCTION_RUNBOOK / chaos runbook |
| secret rotation | `nfr-chaos-secret-rotation` | خدمة تبقى أو تفشل بصراحة وفق [SERVICE_SECRET_ROTATION.md](./SERVICE_SECRET_ROTATION.md) | — |
| worker crash | `nfr-chaos-worker-crash` (stop payroll) | PAY-003 resume؛ لا رواتب مزدوجة | J4 |
| partial region loss | `dr-failover-drill.sh` كـgame day | RTO/RPO OPS-004 | [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md) |
| safe abort | Pause/Cancel تحت latency | حالة نهائية متسقة | PAY-003 runbook |

مالك افتراضي: `owner=platform` للتنبيهات التشغيلية؛ `owner=security` لمسارات تدقيق مرتبطة.
