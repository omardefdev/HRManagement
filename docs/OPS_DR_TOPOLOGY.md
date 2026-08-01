# OPS-004 — طوبولوجيا DR (مواقع)

القرار: [ADR-OPS-004-dr-rto-rpo.md](./ADR-OPS-004-dr-rto-rpo.md). BIA: [OPS_DR_BIA_RTO_RPO.md](./OPS_DR_BIA_RTO_RPO.md). اتصال: [OPS_DR_COMMUNICATION_TREE.md](./OPS_DR_COMMUNICATION_TREE.md). تحقق: [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md).

## نموذج الموقعين

| موقع | الدور | تمثيل في المستودع |
|---|---|---|
| **Primary** | إنتاج حي multi-AZ | `deploy/k8s/overlays/production-ha` + `data/overlays/production-data-ha` |
| **Secondary** | Warm standby | stub: `deploy/k8s/overlays/dr-secondary` + `data/overlays/dr-secondary-data` |

التوفير السحابي للعنقود الثانوي = **OPS-005** عبر `terraform/envs/dr-secondary` ثم `scripts/iac-kustomize-apply.sh dr-secondary`. لا تشارك PVC بين الموقعين.

```text
  [Users] --> DNS hr.example.com
                 |
         +-------+--------+
         |                |
    Primary region    Secondary (warm)
    apps+data HA      bootstrap from
    OPS-001..003      s3://hrm-backups
         |                ^
         +-- offsite -----+
```

## ترتيب الاستعادة (Recovery order)

عند تفعيل الموقع الثانوي بعد فقد الأساسي:

1. **مخزن النسخ** متاح (`hrm-backups`) — تحقق كتالوج OPS-003.
2. **Postgres** — PITR / restore إلى عنقود secondary (`restore-pitr-drill` مكيّف أو bootstrap CNPG من object store).
3. **Keycloak** — JDBC إلى Postgres المستعاد + استيراد realm إن لزم.
4. **RabbitMQ / MinIO** — إعادة إنشاء عنقود؛ مرآة MinIO من `hrm-backups/minio/...`.
5. **Apps + Gateway** — overlay `dr-secondary` بصور SHA نفسها (DEP-001).
6. **DNS cutover** — بعد نجاح `dr-dns-cutover-check.sh` على الهدف + موافقة Go (شجرة الاتصال).

Failback: عكس الاتجاه بعد صحة الموقع الأساسي + [reconciliation](#failback-reconciliation).

## DNS / Failover

| عنصر | قيمة stub |
|---|---|
| الإنتاج | `hr.example.com` → Ingress primary |
| DR host (اختبار) | `hr-dr.example.com` → Ingress secondary (قبل تعميم السجل العام) |
| TTL أثناء تدريب | ≤ 60s |
| فحص | `scripts/dr-dns-cutover-check.sh` على URL الهدف `/health/ready` |

خطوات cutover/failback التشغيلية أيضاً في [PRODUCTION_RUNBOOK.md](./PRODUCTION_RUNBOOK.md) § DR.

**لا** يغيّر أي سكربت سجل DNS تلقائياً دون موافقة موثّقة.

## أوضاع متدهورة (منصة)

| وضع | ما يعمل | ما يُوقف | رسالة مستخدم تقريبية |
|---|---|---|---|
| Read-mostly | استعلام ESS، تحميل ملفات قائمة | إنشاء تعيين/فصل، ترحيل رواتب | «خدمة محدودة — عمليات الكتابة متوقفة مؤقتاً» |
| Payroll freeze | باقي الوحدات | Payroll run / payment batch | «دورة الرواتب معلّقة حتى استعادة المنصة» |
| Auth outage | صفحات صيانة Gateway | كل الوحدات المعتمدة على OIDC | «تعذّر تسجيل الدخول» |

التفاصيل الإجرائية الورقية والاتصال بالموظفين → **BCP-001**. الطاقة/الموقع الفيزيائي → **PHY-001**.

## Failback reconciliation

بعد عودة Primary والـDNS:

1. إيقاف كتابة Secondary (maintenance).
2. مزامنة/استعادة دلتا إن وُجدت (وإلا اعتبار Primary مصدر الحقيقة بعد آخر نسخة متسقة).
3. تشغيل:
   - `scripts/reconcile-tenants.ps1`
   - `scripts/reconcile-employee-orphans.ps1`
   - `scripts/reconcile-file-orphans.ps1`
4. تحقق رحلات P0 من مصفوفة BIA.
5. إغلاق حادثة DR في سجل [OPS_DR_VERIFICATION.md](./OPS_DR_VERIFICATION.md).

## توفير الموقع (OPS-005)

- Terraform: `terraform/envs/dr-secondary` (إقليم ثانوي منفصل، مثلاً `eu-west-1`).
- بعد العنقود: `OWNER=… IMAGE_TAG=… bash scripts/iac-kustomize-apply.sh dr-secondary`.
- قبل توفر حساب: overlays stub تبقى قابلة لـ`kubectl kustomize`؛ تدريبات lab يمكن أن تستخدم namespace `hrmanagement-dr-drill` على عنقود واحد (ليست بديلاً نهائياً عن موقع جغرافي).
