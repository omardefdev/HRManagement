# OBS-003 — طوبولوجيا SIEM والتدقيق

التحقق: [OBS_SIEM_VERIFICATION.md](./OBS_SIEM_VERIFICATION.md). Runbooks: [OBS_SIEM_RUNBOOKS.md](./OBS_SIEM_RUNBOOKS.md).

## مصادر الأحداث

| مصدر | مخزن | سلامة |
|---|---|---|
| Gateway HTTP `/api` `/ui` | `gateway_audit_entries` + hash chain | append-only + chain |
| أحداث typed | `security_audit_events` + JSONL | INSERT فقط |
| وحدات (Auth/File/Payroll) | hooks → SecurityAuditPublisher | correlation |

## التدفق

```text
HTTP/hooks → GatewayAuditQueue / SecurityAuditPublisher
  → DB (hash chain) + optional JSONL
  → AuditWormExporter (batches → Object Lock / local worm dir)
  → SiemDetector (policy/siem/*.yaml) → alert-sink
  → obs-audit-chain-verify
```

## أدوار

| دور | صلاحية |
|---|---|
| `hrm_audit_writer` | INSERT على جداول التدقيق |
| `hrm_audit_reader` | SELECT فقط (تحقيق) |
| حساب التطبيق | يُقيَّد كاتب؛ لا UPDATE/DELETE |

## قناة التنبيه

نفس [`alert-sink`](../docker/alert-sink/) من OBS-002 مع labels `owner=security`.
