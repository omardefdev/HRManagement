# OPS-001 — طوبولوجيا HA (multi-AZ)

مرجع معماري لبطاقة **OPS-001**. القرار: [ADR-OPS-001-orchestrator.md](./ADR-OPS-001-orchestrator.md). التحقق التشغيلي: [OPS_HA_VERIFICATION.md](./OPS_HA_VERIFICATION.md).

## نطاق البطاقة

| داخل OPS-001 | خارج النطاق (بطاقة لاحقة) |
|---|---|
| Kubernetes للمنسّق؛ multi-AZ داخل إقليم واحد | إقليم ثانوي / DR → **OPS-004** ([OPS_DR_TOPOLOGY.md](./OPS_DR_TOPOLOGY.md)) |
| Gateway + Web apps (stateless) replicas / HPA / PDB / anti-affinity | HA بيانات مفصّلة → **OPS-002** ([OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md)) |
| Ingress + TLS أمام Gateway؛ مثال تكامل WAF | Terraform / IaC سحابي كامل → **OPS-005** |
| Startup / live / ready probes؛ graceful shutdown | تغيير منطق الأعمال |

Compose للتطوير/مضيف واحد يبقى صالحاً؛ **الإنتاج HA = k8s** عبر [`deploy/k8s/`](../deploy/k8s/).

## مناطق التوفر (AZ)

التصميم يفترض **ثلاث مناطق توفر** على الأقل داخل إقليم واحد (مثلاً `topology.kubernetes.io/zone=az-a|az-b|az-c`).

```text
                    Internet / clients
                            |
                     [ Cloud WAF / TLS ]
                            |
                      Ingress NGINX
                            |
                   hr-gateway Service
                     (replicas: 3)
                    /       |       \
              pods on AZ-a AZ-b AZ-c  (anti-affinity)
                            |
              ClusterIP → module Services (replicas ≥ 2)
                            |
          Data plane HA (OPS-002): namespace hrmanagement-data
          Postgres CNPG + pooler / RabbitMQ 3 / Keycloak ISPN / MinIO 4
          → [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md)
```

## طبقات الشبكة (مواءمة مع NETWORK_MATRIX)

| طبقة | في Compose | في k8s (OPS-001 / OPS-002) |
|---|---|---|
| Edge | شبكة `edge` + منافذ Gateway | Ingress → Gateway فقط؛ TLS عند الحافة |
| Application | `application` internal | Pod Network / ClusterIP بين الخدمات (`hrmanagement`) |
| Data | `data` internal | Namespace **`hrmanagement-data`** — [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md) |

وحدات التطبيق **ClusterIP فقط** — لا NodePort/LoadBalancer لكل خدمة.

## النسخ والـHPA الافتراضية (production-ha)

| عبء | replicas min | HPA max | ملاحظة |
|---|---|---|---|
| `hr-gateway` | 3 | 6 | required anti-affinity على hostname؛ preferred على zone |
| `hr-core-web`, `payroll-web`, `hr-contracts-web` | 2 | 6 | حرجة |
| باقي Web apps | 2 | 4 | حد أدنى 2 في الإنتاج |
| Jobs مرة واحدة (migrate) | 1 | — | **لا HPA** |
| ExportJob / PayrollRunJob workers | داخل Deployment الخدمة | — | claim/lease يمنع تكرار العمل |

**هدف HPA:** متوسط CPU ≈ 70% (`targetCPUUtilizationPercentage: 70`).

**PDB:** `minAvailable: 1` للخدمات بنسختين؛ `maxUnavailable: 1` للـGateway (3 نسخ).

**Resources (افتراضي):** requests `cpu: 100m` / `memory: 256Mi`؛ limits `cpu: 1000m` / `memory: 1Gi` (Gateway memory limit `1.5Gi`).

## Anti-affinity

- **Gateway:** `podAntiAffinity` **required** على `kubernetes.io/hostname`؛ **preferred** على `topology.kubernetes.io/zone`.
- **Web apps:** preferred على zone + hostname (لا تمنع الجدولة إن ضاقت السعة).

## YARP وتعدد الوجهات

على Kubernetes يكفي عنوان **Service DNS** واحد لكل cluster (مثال `http://hr-core-web:8080`) — الـkube-proxy يوزّع على الـEndpoints. سياسة `PowerOfTwoChoices` جاهزة عند إضافة وجهات متعددة (ConfigMap أو headless). انظر ConfigMap `hr-gateway-yarp` في overlay الإنتاج.

## Jobs تحت المقياس

- **Migrate:** Job مرة واحدة قبل الـrollout (`scripts/run-migrations.sh` / efbundle) — ليس init-container لكل replica.
- **ExportJob / PayrollRunJob:** صف DB + lease/claim (PERF-001/004) — توسيع الـpods لا يكرر نفس المهمة دون claim.

## WAF (حد أدنى إلزامي للإنتاج)

تكامل عند الحافة (cloud WAF أو ModSecurity على Ingress). قواعد دنيا موثّقة:

1. TLS only (إعادة توجيه HTTP→HTTPS).
2. Rate limit على مسارات المصادقة والـAPI العامة.
3. حظر مسارات إدارة داخلية من الإنترنت.

مثال تعليقات/annotations في `deploy/k8s/overlays/production-ha/ingress.yaml`. تنفيذ مزوّد سحابي كامل يبقى stub حتى OPS-005.

## أسرار واتصال البيانات

الاتصال بـPostgres/RabbitMQ/Keycloak/MinIO عبر **Secrets / ConfigMap**. تكرار وصحة مستوى البيانات = **OPS-002** — انظر [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md) و[`deploy/k8s/data/`](../deploy/k8s/data/).

## إزالة `container_name`

مسار HA لا يستخدم أسماء حاويات ثابتة. للتطوير: Compose قد يبقي `container_name` للراحة. للتحقق من عدم فرض أسماء ثابتة على مسار الإنتاج انظر [`docker-compose.ha-forbidden.yml`](../docker-compose.ha-forbidden.yml) والسياسة في [PRODUCTION_RUNBOOK.md](./PRODUCTION_RUNBOOK.md).

## بوابة DEP-001 قبل أول rollout k8s

انظر [DEP_001_RELEASE_GATE.md](./DEP_001_RELEASE_GATE.md): صور SHA/digest، migrate مقفول، health gate، rollback — نفس سجل GHCR يُستهلك في k8s عبر `IMAGE_TAG`.
