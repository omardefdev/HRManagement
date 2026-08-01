# OPS-004 — شجرة اتصال وقرار DR

مرجع الطوبولوجيا: [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md). التحقق: [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md).

## الأدوار

| دور | مسؤولية | Go/No-Go |
|---|---|---|
| **Incident Commander (IC)** | قيادة حادثة فقد الموقع؛ إعلان وضع DR | يملك قرار بدء failover بعد التشاور |
| **Platform / SRE** | تنفيذ الترتيب التقني والسكربتات؛ قياس الزمن | يوصي؛ لا يقطع DNS وحده |
| **DBA** | صحة Postgres PITR/restore؛ سلامة الكتالوج | يوقف التقدم إن فشل التحقق |
| **Security** | أسرار الموقع الثانوي؛ نطاق TLS/WAF | يوقف إن كان cutover غير آمن |
| **Business Owner** | قبول أثر الأعمال / وضع متدهور | يوافق على payroll freeze وغيره |
| **Communications** | رسائل المستخدمين/الجهات (مع BCP-001) | ينفّذ بعد قرار IC |

املأ الأسماء/النوّاب في سجل التدريب المحلي (لا تُلتزم أسرار اتصال في git).

## قنوات (حد أدنى)

1. قناة حادثة فورية (مثلاً غرفة عمليات / جسر صوتي).
2. قناة كتابة قرارات (تذكرة/سجل في `OPS_DR_VERIFICATION` نتائج).
3. قناة إعلام خارجي عبر إجراءات **BCP-001**.

## معايير Go — Failover إلى Secondary

- [ ] الموقع الأساسي غير متاح فوق عتبة IC (فقدان إقليم / تقدير RTO سيتجاوز هدف الرحلة P0).
- [ ] كتالوج النسخ / `hrm-backups` متاح (`verify-backup-catalog` أو مكافئ).
- [ ] Secondary cluster جاهز أو lab drill namespace موافق عليه.
- [ ] DBA يؤكد إمكانية استعادة Postgres ضمن الهدف.
- [ ] Security يوافق على أسرار/TLS الهدف.
- [ ] Business Owner على علم بالوضع المتدهور إن لزم.
- [ ] `dr-dns-cutover-check.sh` ناجح على URL الهدف **قبل** تغيير السجل العام (أو على `hr-dr` ثم ترقية).

## معايير Go — Failback إلى Primary

- [ ] Primary مستعاد وصحته مثبتة (HA verify + رحلات P0).
- [ ] نافذة صيانة معلنة؛ Secondary في وضع قراءة/إيقاف كتابة.
- [ ] Reconciliation (`dr-failback-reconcile.sh`) مكتمل دون فجوات غير مفسّرة.
- [ ] DNS عاد بعد فحص صحة Primary.
- [ ] IC يغلق حادثة DR في سجل النتائج.

## No-Go (أمثلة)

- مخزن النسخ غير متاح أو checksum فاشل.
- لا مالك أعمال لقرار إيقاف الرواتب.
- Secondary يشير إلى PVC/أسرار الموقع الأساسي بالخطأ.
