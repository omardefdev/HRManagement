# ADR-OBS-002 — SLI/SLO وتنبيهات قابلة للتنفيذ

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OBS-002 |
| التاريخ | 2026-07-28 |

## السياق

OBS-001 وفّر مقاييس/تتبع ولوحات RED دون SLO رحلات، ولا burn alerts، ولا قناة Alertmanager حقيقية، ولا black-box، ولا ملكية/runbook على التنبيهات.

## القرار

1. **أربع رحلات SLO:** J1 GatewayAvailability، J2 ApiLatency، J3 MessagingFreshness، J4 PayrollCorrectnessSignal — التفاصيل في [OBS_SLO_CATALOG.md](./OBS_SLO_CATALOG.md).
2. **أهداف:** availability 99.5% / 30d لـJ1–J2؛ P95 &lt; 2s لـJ2؛ freshness outbox؛ payroll fail ratio &lt; 1%.
3. **Burn alerts:** نافذتان (سريع page / بطيء ticket) على availability.
4. **قناة:** Alertmanager → webhook `alert-sink` (قابل للاختبار) أو `ALERTMANAGER_WEBHOOK_URL` خارجي.
5. **Black-box** لـ`/health/ready` (+ مسار API جاهز).
6. **كل alert:** `owner` + `runbook_url` → [OBS_ALERT_RUNBOOKS.md](./OBS_ALERT_RUNBOOKS.md).
7. **خارج النطاق:** SIEM (OBS-003 Done)؛ load/soak نُفّذ في **NFR-001** ([ADR-NFR-001-capacity-load.md](./ADR-NFR-001-capacity-load.md)).

## العواقب

- REL-003 وNFR-002 يبنيان على تنبيهات وميزانيات خطأ موثّقة.
- Fire drill عبر `scripts/obs-alert-fire-drill.sh` شرط إغلاق.
