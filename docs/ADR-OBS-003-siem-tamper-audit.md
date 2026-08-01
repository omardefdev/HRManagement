# ADR-OBS-003 — SIEM وتدقيق غير قابل للتلاعب

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OBS-003 |
| التاريخ | 2026-07-28 |

## السياق

تدقيق البوابة متين (طابور/overflow/DLQ + رفض EF) لكن بلا سلسلة تجزئة، بلا WORM للتدقيق، بلا قواعد كشف أمني موحّدة، والـretention يمكنه DELETE خام.

## القرار

1. **Hash chain** على `gateway_audit_entries` (`PrevHash` + `EventHash` SHA-256).
2. **WORM export** لدفعات JSONL إلى دلو/مسار بـObject Lock (lab: مجلد محلي + catalog؛ prod: S3/MinIO lock).
3. **`security_audit_events`** + hooks لـBulkLookup / FileAccess / RoleChange / PayrollOverride / PrivilegedGateway.
4. **SIEM rules lite** تحت `policy/siem/` + detector → alert-sink (OBS-002).
5. **Legal hold** يمنع purge؛ retention يتطلب `AllowPurge` + لا hold + تصدير مسبق.
6. **أدوار DB:** `hrm_audit_writer` (INSERT) / `hrm_audit_reader` (SELECT).
7. **إغلاق AUD-001** مع أدلة المتانة + السلسلة.
8. **خارج النطاق:** منتج SIEM تجاري كامل؛ استبدال Seq/Tempo.

## العواقب

- EVD/وطني يستهلك التصدير والسلسلة لاحقاً.
- تنبيهات أمنية تصل نفس قناة OBS-002 مع `owner=security`.
