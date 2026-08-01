# NFR-002 — تقويم Game Days (ربع سنوي)

| الربع | فرضية | سيناريو | نتيجة | تذكرة | Retest |
|---|---|---|---|---|---|
| قالب Q | Latency على Rabbit يرفع outbox age ويُطلق تنبيه قبل فقد أحداث | `inject rabbit latency` + مراقبة J3 + `obs-alert-fire-drill` | انظر `chaos/results/baseline-lab/` | — | بعد إصلاح إن وُجد |
| Q+DR | فقد منطقة جزئي ضمن RTO/RPO | `scripts/dr-failover-drill.sh` (OPS-004) | يُملأ يوم التمرين | — | — |
| Q+Pay | إيقاف عامل رواتب لا يضاعف السطور | `nfr-chaos-worker-crash` + resume | يُملأ | — | — |
| Q+Id | تدوير سر لا يُسقط المنصة صامتاً | `nfr-chaos-secret-rotation` | يُملأ | — | — |
| Q+REL003 | مناوبة تستجيب لصفحة Sev2 ضمن MTTA وتُغلق ضمن MTTR | `scripts/rel003-incident-game-day.ps1` + `testdata/rel003/game-day/` | ledger تحت `out/` | — | عند فشل MTTA/MTTR |

## قواعد

1. كل game day: فرضية مكتوبة **قبل** الحقن.
2. نتائج خام تحت `chaos/results/<stamp>-.../`.
3. تحديث [NFR_CHAOS_RUNBOOKS.md](./NFR_CHAOS_RUNBOOKS.md) / OBS runbooks إن تغيّر السلوك.
4. Retest إلزامي إن فُتحت تذكرة إصلاح.

يغذي **NAT-006** و**SRE-003** دون استبدال مسرح الحوادث الكامل.
