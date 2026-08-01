# OBS-002 — Runbooks للتنبيهات

الطوبولوجيا: [OBS_ALERTING_TOPOLOGY.md](./OBS_ALERTING_TOPOLOGY.md). الكتالوج: [OBS_SLO_CATALOG.md](./OBS_SLO_CATALOG.md).

لكل تنبيه: **المعنى** → **تشخيص** → **فعل** → **تصعيد** → **silence**.

---

## J1GatewayAvailabilityBurnFast

- **المعنى:** حرق سريع لميزانية توفر Gateway (black-box).
- **تشخيص:** `probe_success{job="probe_gateway_ready"}`؛ حالة حاوية `hr-gateway`؛ Tempo لأخطاء حديثة.
- **فعل:** استعادة Gateway/شبكة؛ التحقق من `/health/ready`.
- **تصعيد:** Platform on-call إن استمر &gt;15m.
- **Silence:** فقط أثناء صيانة مجدولة عبر `obs-silence-maintenance.sh`.

## J1GatewayAvailabilityBurnSlow

- كـFast لكن نافذة أطول — تحقيق اتجاه قبل paging إضافي.

## J2ApiAvailabilityBurnFast / J2ApiAvailabilityBurnSlow

- **المعنى:** نسبة 5xx على `hr-gateway` تحرق الميزانية.
- **تشخيص:** لوحة RED؛ سجلات Seq؛ خدمات backend عبر YARP.
- **فعل:** إصلاح الخدمة الخلفية أو تخفيف الحمل؛ لا تُخفِ الخطأ بتعطيل التنبيه.

## J2ApiLatencyHigh

- **المعنى:** P95 Gateway &gt; 2s لمدة 10m.
- **تشخيص:** USE dashboard؛ DB/MQ؛ HPA.
- **فعل:** توسيع/تحسين استعلام؛ التحقق من NFR لاحقاً.

## J3MessagingFreshnessDegraded / OutboxPendingHigh

- **المعنى:** outbox متراكم أو عمر رسالة مرتفع.
- **تشخيص:** لوحة Messaging؛ consumers؛ RabbitMQ ready.
- **فعل:** إعادة تشغيل consumer؛ فحص أخطاء `*_error` queues.

## J4PayrollFailRatioHigh

- **المعنى:** نسبة فشل موظفي تشغيل راتب &gt;1% أثناء النشاط.
- **تشخيص:** مقاييس `hrm.payroll.run.*`؛ سجلات Payroll؛ حالة التشغيل.
- **تصعيد:** Payroll owner.

## ServiceDown / BlackboxGatewayProbeDown

- **المعنى:** عرض scrape أو probe فاشل (عرض بجانب SLO).
- **فعل:** استعادة العملية أولاً ثم التحقق من J1 burn.

## RabbitMqExporterDown / PostgresExporterDown / HighHttpServerDuration / InboxConsumerStalled

- أعراض داعمة — راجع المالك `platform`؛ لا paging إلا إذا اقترنت برحلة.

---

## تصعيد عام

1. Owner على الـlabel يستجيب للقناة (alert-sink / webhook خارجي).
2. بعد تكرار Alertmanager دون حل → SRE on-call ([REL_003_ONCALL_ROSTER.md](./REL_003_ONCALL_ROSTER.md)); افتح حادثة حسب [REL_003_INCIDENT_PROCESS.md](./REL_003_INCIDENT_PROCESS.md).
3. لا تُغلق البطاقة بصمت — سجّل في حادثة.
