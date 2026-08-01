# OPS-004 — تحقق DR (tabletop + technical)

مراجع: [OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md)، [OPS_DR_BIA_RTO_RPO.md](./OPS_DR_BIA_RTO_RPO.md)، [OPS_DR_COMMUNICATION_TREE.md](./OPS_DR_COMMUNICATION_TREE.md).  
IaC للموقع الثانوي = **OPS-005**. استمرارية ورقية = **BCP-001**. منشآت = **PHY-001**.

## جدول التمارين

| نوع | الحد الأدنى | سكربت / وثيقة |
|---|---|---|
| Tabletop فقد إقليم | كل 6 أشهر | قائمة أدناه |
| Technical failover (lab أو secondary) | كل 3 أشهر | `scripts/dr-failover-drill.sh` |
| Failback + reconcile | مع كل technical ناجح | `scripts/dr-failback-reconcile.sh` |
| DNS check قبل cutover | كل تدريب تقني | `scripts/dr-dns-cutover-check.sh` |

## قائمة Tabletop

- [ ] سيناريو: فقد إقليم Primary (شبكة/منشأة).
- [ ] IC يعيّن الأدوار من شجرة الاتصال.
- [ ] قرار Go/No-Go وفق معايير الوثيقة.
- [ ] اختيار وضع متدهور (إن لزم) وربطه بـBCP-001.
- [ ] مراجعة ترتيب الاستعادة وأهداف RTO الرحلات P0.
- [ ] تسجيل الفجوات → بطاقات (PHY/BCP/OPS-005/…).

## قائمة Technical failover

- [ ] `kubectl kustomize` لـ`dr-secondary` و`dr-secondary-data` ناجح
- [ ] Prechecks كتالوج/HA (أو توثيق التخطي في lab)
- [ ] `bash scripts/dr-failover-drill.sh` (و`DR_APPLY=1` عند وجود عنقود ثانٍ)
- [ ] استعادة بيانات من `hrm-backups` ضمن الهدف
- [ ] `DR_TARGET_URL=https://hr-dr.example.com bash scripts/dr-dns-cutover-check.sh` → 200
- [ ] DNS cutover يدوي بعد Go — قياس RTO الفعلي
- [ ] رحلة تسجيل دخول + بوابة smoke

## قائمة Failback

- [ ] Primary صحّي
- [ ] `bash scripts/dr-failback-reconcile.sh`
- [ ] فحص Primary ثم DNS عودة
- [ ] لا فجوات reconcile غير مفسّرة

## سجل النتائج

| التاريخ | نوع | RPO ملحوظ | RTO ملحوظ | النتيجة | إجراءات / بطاقات |
|---|---|---|---|---|---|
| | tabletop | — | — | | |
| | technical | | | | |
| | failback | | | | |

بعد أول technical ناجح: حدّث خلايا **measured** في [OPS_DR_BIA_RTO_RPO.md](./OPS_DR_BIA_RTO_RPO.md).

## إغلاق البطاقة

- [ ] ADR + BIA + topology + communication tree + verification
- [ ] overlays stub تُبنى بـkustomize
- [x] إشارة: **توفير الموقع كـIaC = OPS-005** (`terraform/envs/dr-secondary`)
