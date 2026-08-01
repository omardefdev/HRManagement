# OBS-003 — تحقق SIEM والتدقيق غير القابل للتلاعب

## بوابة الإغلاق

| تحقق | أمر / اختبار | نتيجة متوقعة |
|---|---|---|
| سلسلة سليمة | `DRY_RUN=1 bash scripts/obs-audit-chain-verify.sh` + unit `AuditHashChain_*` | ok / pass |
| كشف تبديل | `DRY_RUN=1 bash scripts/obs-audit-tamper-drill.sh` + unit tamper | verify يفشل بعد تبديل |
| منع EF | `GatewayAuditEntries_RejectUpdateAndDelete` | InvalidOperationException |
| كشف SIEM | `DRY_RUN=1 bash scripts/obs-siem-detection-drill.sh` | payload إلى alert-sink |
| WORM | `POST /api/platform/audit/worm-export` أو exporter | `audit-export-catalog.jsonl` + `.objectlock` |
| Legal hold | hold نشط + `AllowPurge` | retention لا يحذف |
| أدوار DB | `scripts/sql/obs003-audit-db-roles.sql` | reader SELECT فقط |

## أدلة مرتبطة

- ADR: [ADR-OBS-003-siem-tamper-audit.md](./ADR-OBS-003-siem-tamper-audit.md)
- طوبولوجيا: [OBS_SIEM_TOPOLOGY.md](./OBS_SIEM_TOPOLOGY.md)
- تهديد: [OBS_AUDIT_THREAT_MODEL.md](./OBS_AUDIT_THREAT_MODEL.md)
- Runbooks: [OBS_SIEM_RUNBOOKS.md](./OBS_SIEM_RUNBOOKS.md)
- قواعد: `policy/siem/`
- AUD-001 متانة الطابور تُغلق مع إثبات السلسلة هنا.

## إشارة وطنية

تصدير WORM + السلسلة consumable لاحقاً من EVD-* دون إعادة بناء SIEM تجاري في هذه البطاقة.
