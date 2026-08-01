# ADR-FUN-005 — إغلاق الهوية والأصول وتقنية المعلومات

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | FUN-005 (البطاقة 54) |
| التاريخ | 2026-07-29 |
| الاعتماديات | INT-003 Done، FILE-002 Done |

## السياق

CRUD بطاقة/أصل/أجهزة موجود مع تفاعل فصل جزئي، لكن بلا checklist تسليم، بلا إثبات مستلم، party ACK فارغ، أصول بحائزين متزامنين محتملين، وتزويد IT بلا حالة Failed/Retry. UAT مؤجّل.

## القرار

| الموضوع | الاختيار |
|---|---|
| مصفوفة الرحلة | `docs/fun-identity/journey-matrix.json` — IDCard / Assets / ITProvision |
| Orchestration | party Execute + أحداث قائمة — **لا** Automatonymous saga جديد |
| بطاقة | CardChecklist + soft-cancel طلب؛ تسليم بدليل إلزامي؛ party revoke + SeparationCompleted شبكة أمان |
| أصول | فهرس فريد للتعيين المفتوح؛ Lost + IsPrivileged |
| IT | ProvisioningStatus Failed/Retry؛ فصل يفصل الأجهزة |
| Auth Retry | SyncFromKeycloak القائم (لا إعادة بناء IAM-004) |
| UAT | ReadyForSign-off (`FUN_005_UAT_PACK.md`) |
| خارج النطاق | FUN-006؛ طابعات فعلية؛ مخزون وطني كامل |

## العواقب

- لا بطاقة Active بعد فصل (party + fan-out).
- لا تعيين أصل ثانٍ مفتوح.
- فشل التزويد مرئي ويُعاد.
