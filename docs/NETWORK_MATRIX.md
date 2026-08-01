# مصفوفة الشبكات والمنافذ (SEC-001)

مصدر الحقيقة لإغلاق السطح الهجومي في الإنتاج. التطوير المحلي (`docker-compose.yml` + `docker-compose.services.yml`) يبقى بمنافذ مريحة؛ القيود تُطبَّق فقط مع `docker-compose.production.yml`.

## أوامر التشغيل

```bash
docker compose \
  -f docker-compose.yml \
  -f docker-compose.services.yml \
  -f docker-compose.observability.yml \
  -f docker-compose.production.yml \
  up -d
```

يجب أن يكون `docker-compose.production.yml` **آخر** ملف حتى تفوز عمليات `ports: !override` على منافذ المراقبة.

التحقق من المنافذ المنشورة:

```bash
docker compose \
  -f docker-compose.yml \
  -f docker-compose.services.yml \
  -f docker-compose.observability.yml \
  -f docker-compose.production.yml \
  config --format json | jq '.services | to_entries[] | {name: .key, ports: .value.ports}'
```

## الشبكات المعزولة

| الشبكة | نوع | الغرض |
|---|---|---|
| `edge` | bridge (عامة) | نقطة الدخول العامة: Gateway + Keycloak |
| `application` | bridge **internal** | تواصل الخدمات مع بعضها ومع أدوات المراقبة |
| `data` | bridge **internal** | PostgreSQL / RabbitMQ / MinIO / Keycloak DB |
| `observability` | bridge **internal** | Seq / OTEL / Tempo / Prometheus / Grafana / Alertmanager / blackbox / alert-sink / exporters |

`internal: true` يمنع الخروج إلى الإنترنت من تلك الشبكات ويمنع الربط العرضي بحاويات خارج المشروع.

للإنتاج **HA متعدد المناطق** انظر [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md) و`deploy/k8s/` (Ingress → Gateway فقط؛ التطبيقات ClusterIP).

## توزيع الخدمات على الشبكات

| الخدمة | الشبكات | منافذ Host |
|---|---|---|
| `hr-gateway` | edge, application, data | `${GATEWAY_HTTP_PORT:-5000}:8080` (عامة) |
| `keycloak` | edge, data | `${KEYCLOAK_HTTP_PORT:-8080}:8080` (إدارية — تقييد بـ VPN/firewall) |
| خدمات HR (14) | application, data | **لا يوجد** |
| `postgresql`, `rabbitmq`, `minio` | data | **لا يوجد** |
| `postgres-backup` | data | **لا يوجد** |
| `seq`, `otel-collector`, `prometheus` | observability, application | **لا يوجد** |
| `tempo` | observability | **لا يوجد** (query داخلي / Compose lab: 3200) |
| `alertmanager` | observability | **لا يوجد** |
| `grafana` | observability | `127.0.0.1:${GRAFANA_HTTP_PORT:-3000}:3000` (localhost فقط) |
| `rabbitmq-exporter` | observability, data | **لا يوجد** |
| `postgres-exporter` | observability, data | **لا يوجد** |
| `blackbox-exporter` | observability, application | **لا يوجد** (Compose lab: 9115) |
| `alert-sink` | observability | **لا يوجد** (Compose lab: 8089) |

## مصفوفة المصدر → الوجهة

| المصدر | الوجهة | المنفذ | البروتوكول | الغرض |
|---|---|---|---|---|
| الإنترنت / مستخدمون | Gateway | 5000 (قابل للتغيير) | HTTP(S) عبر reverse proxy أمامي | نقطة الدخول الوحيدة للتطبيق |
| متصفح / OIDC | Keycloak | 8080 | HTTP(S) | تسجيل الدخول وإعادة التوجيه |
| Gateway | خدمات HR | 8080 | HTTP داخلي | YARP reverse proxy |
| Gateway | Keycloak | 8080 | HTTP داخلي | metadata / token |
| Gateway | PostgreSQL | 5432 | TCP | Gateway audit DB |
| خدمات HR | PostgreSQL | 5432 | TCP | قواعد الخدمات |
| خدمات HR | RabbitMQ | 5672 | AMQP | الرسائل |
| خدمات HR | MinIO | 9000 | S3 API | الملفات |
| خدمات HR | Keycloak | 8080 | HTTP | JWT authority |
| خدمات HR | Seq | 80/5341 | HTTP | السجلات |
| خدمات HR | otel-collector | 4317 | OTLP gRPC | التتبع والمقاييس |
| Prometheus | خدمات HR | 8080 `/metrics` | HTTP | scrape |
| Prometheus | rabbitmq-exporter | 9419 | HTTP | scrape |
| rabbitmq-exporter | RabbitMQ | 15672 | HTTP | management API |
| Grafana | Prometheus | 9090 | HTTP | لوحات المراقبة |
| مشغّل على المضيف (VPN) | Grafana | 3000 على loopback | HTTP | إدارة المراقبة |

## ما يجب ألا يظهر من خارج المضيف

فحص من شبكة خارجية (أو من مضيف آخر على نفس LAN) يجب ألا يرى:

- منافذ الخدمات `5010`–`5140`
- PostgreSQL `5433` / `5432`
- RabbitMQ `5672` / `15672` / `5673` / `15673`
- MinIO `9000` / `9001`
- Seq `5341`, OTEL `4317`, Prometheus `9090`, Alertmanager `9093`, RabbitMQ-Exporter `9419`
- Grafana إلا عبر `127.0.0.1` على المضيف نفسه أو عبر نفق SSH/VPN

المنافذ المعتمدة للكشف (مع جدار ناري deny-by-default أمامها):

1. **Gateway** — عامة للتطبيق
2. **Keycloak** — عامة لـ OIDC (يفضّل تقييدها أو وضعها خلف نفس الـ TLS terminator)
3. **Grafana** — loopback فقط؛ الوصول الإداري عبر bastion/VPN

## طبقات حماية إضافية في التطبيق

| المسار | الحماية |
|---|---|
| `/health/live` | مجهول؛ لا يكشف تفاصيل (دائماً Healthy) |
| `/health`, `/health/ready` | مجهول للـ orchestrator؛ في الإنتاج تُرجع `{ "status": "Healthy" }` فقط بلا أسماء فحوصات |
| `/health/upstreams/*` على Gateway | يتطلب سياسة `Gateway.Dashboard` (مصادقة) |
| `/metrics` | مسموح فقط من عناوين loopback / RFC1918 / ULA |

## هوية الخدمة داخل شبكة `application` (IAM-004)

الشبكات المعزولة تمنع الوصول من الخارج؛ **داخل** `application` تُغلق الفجوة بهوية JWT للخدمة (وليست mTLS في هذه البطاقة):

| من → إلى | المسار | الهوية المطلوبة |
|---|---|---|
| IT.Infra → Auth.Directory | `/api/internal/directory/sync-employee` | `svc-it-infra` + `directory.sync` |
| Payroll → HR.Core | `/api/lookup/**` | `svc-payroll` + `hr-core.lookup` (أو مستخدم تفاعلي) |
| HR.* / IDCard / Archive → File.Storage (عبر Gateway) | `/api/files/{id}/references` | عميل خدمة معتمد + `file-storage.references` |

المخزون الكامل: [SERVICE_TRUST_INVENTORY.md](./SERVICE_TRUST_INVENTORY.md).

## سياسة الجدار الناري المقترحة (مرجع تشغيلي)

```
DEFAULT DENY inbound
ALLOW tcp/${GATEWAY_HTTP_PORT} from trusted edge / load balancer
ALLOW tcp/${KEYCLOAK_HTTP_PORT} from trusted edge / load balancer
# لا تفتح منافذ Docker الداخلية أو Grafana على الواجهة العامة
```

## Kubernetes NetworkPolicies (SEC-001)

تحت `deploy/k8s/base/network-policies/`:

| Policy | الغرض |
|---|---|
| `deny-all-default` | deny-by-default لكل pods (Ingress + Egress) |
| `allow-dns` | DNS إلى kube-system |
| `allow-ingress-to-gateway` | دخول edge إلى Gateway:8080 فقط |
| `allow-gateway-to-apps` | Gateway → خدمات التطبيق |
| `allow-apps-to-data` | تطبيقات → PostgreSQL / RabbitMQ / MinIO / Keycloak / OTEL / Seq |
| `allow-observability-scrape` | Prometheus scrape + Keycloak edge |

لا حركة جانبية بين pods غير الموثقة في المصفوفة أعلاه.

## نقاط النهاية المجهولة المعتمدة

| المسار | الحالة |
|---|---|
| `/health/live`, `/health`, `/health/ready` | مجهول (orchestrator) |
| `Home/Error` | مجهول (صفحة خطأ) |
| `/api/platform/audit/security-events` | **مصادق** — `platform-admin` فقط (SEC-001) |
| `/health/upstreams/*` | مصادق (`Gateway.Dashboard`) |

## التحقق

1. `powershell -File scripts/sec001-port-scan.ps1` — تحقق ثابت + `docker compose config`.
2. `dotnet test --filter Sec001PortScanTests` — اختبارات CI.
3. Port scan من خارج المضيف: لا يظهر إلا Gateway (وKeycloak إن نُشر).
4. محاولة `curl http://host:5010/health` تفشل (connection refused).
5. `curl http://gateway/health/upstreams/hr-core` بدون جلسة → 401/302/403.
6. الوصول إلى Grafana فقط عبر `http://127.0.0.1:3000` على المضيف.
7. `kubectl apply -k deploy/k8s/base` — NetworkPolicies تمنع الحركة الجانبية.
