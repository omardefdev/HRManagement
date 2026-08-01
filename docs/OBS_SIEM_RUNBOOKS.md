# OBS-003 — Runbooks SIEM

الطوبولوجيا: [OBS_SIEM_TOPOLOGY.md](./OBS_SIEM_TOPOLOGY.md). القواعد: `policy/siem/*.yaml`.

## تنبيه SiemBulkLookup

1. راجع `security_audit_events` لـ `BulkLookup` في النافذة الزمنية.
2. طابق `ActorSub` + `CorrelationId` مع `gateway_audit_entries`.
3. إن كان شرعياً (تكامل/تقرير) وثّق استثناء مؤقت؛ وإلا عطّل الحساب وافتح تذكرة أمنية.

## تنبيه SiemRoleChange / SiemPayrollOverride

1. تحقق من SoD وموافقات ثانية إن وُجدت.
2. لا تُلغَ الأحداث؛ صدّر دفعة WORM إن التحقيق طويل الأمد.

## تنبيه SiemFileAccessBurst

1. راجع `FileAccessAuditLogs` + أحداث `FileAccessSensitive`.
2. فرض legal hold على نطاق زمني إن لزم.

## كسر سلسلة التجزئة

1. شغّل `scripts/obs-audit-chain-verify.sh`.
2. لا تُصلح الهاش يدوياً — استرجع من WORM/catalog أو PITR.
3. بلّغ `owner=security` عبر alert-sink.
