# NFR-002 — Runbooks أثناء حقن الأعطال

الكتالوج: [NFR_CHAOS_CATALOG.md](./NFR_CHAOS_CATALOG.md). تنبيهات SLO: [OBS_ALERT_RUNBOOKS.md](./OBS_ALERT_RUNBOOKS.md).

## Latency / Down على PG أو Rabbit

1. أكّد الحقن: `nfr-chaos-inject` ثم راقب 5xx / outbox age.
2. لا تُصلح البيانات يدوياً أثناء الحقن — انتظر `nfr-chaos-restore`.
3. بعد الاستعادة: تحقق outbox drain وJ3؛ payroll resume إن توقفت.
4. إن أطلق burn alert: اتبع OBS runbook ثم أغلق بعد استعادة SLO.

## Packet loss

1. حقن `loss` لفترة قصيرة (≤5 دقائق).
2. راقب retries (jitter) و`*_error` — لا إعادة تشغيل عشوائية للمستهلكين.
3. استعادة + تحقق عدم تكرار أثر (Inbox / قيود فريدة).

## Clock skew

1. JWT خارج نافذة `ClockSkew` (Keycloak extensions ≈ 2m) → 401 متوقع.
2. صحّح NTP على المضيف؛ لا توسّع ClockSkew لإخفاء انحراف ساعات الإنتاج.

## Expired cert

1. استخدم أثر `nfr-chaos-expired-cert` على terminator مخبري فقط.
2. يجب فشل TLS ظاهر؛ ممنوع fallback صامت لـHTTP على مسارات سرية.

## Secret rotation

اتبع [SERVICE_SECRET_ROTATION.md](./SERVICE_SECRET_ROTATION.md) + `nfr-chaos-secret-rotation.ps1`.

## Worker crash / Safe abort

1. `nfr-chaos-worker-crash.sh` ثم PAY-003 resume.
2. `nfr-chaos-safe-abort.ps1` تحت latency — Pause/Cancel يجب أن يترك حالة متسقة.

## Partial region

نفّذ [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md) / `dr-failover-drill.sh` كصف في [NFR_GAME_DAY_CALENDAR.md](./NFR_GAME_DAY_CALENDAR.md).
