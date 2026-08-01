# ADR-OPS-001 — منسّق التشغيل عالي التوافر

| الحقل | القيمة |
|---|---|
| الحالة | Accepted |
| البطاقة | OPS-001 |
| التاريخ | 2026-07-28 |

## السياق

المنصة تعمل اليوم عبر Docker Compose على مضيف واحد مع `container_name` ثابت ونسخة واحدة لكل خدمة. معايير OPS-001 تتطلب فقد node/instance دون إسقاط الخدمة فوق SLO، ونشراً rolling، وautoscaling دون تكرار jobs.

## القرار

1. **المنسّق للإنتاج HA: Kubernetes** (ليس Docker Swarm).
2. **Compose** يبقى للتطوير المحلي وstaging أحادي المضيف (غير HA).
3. **نطاق «المناطق» في OPS-001 = multi-AZ داخل إقليم واحد** (≥3 مناطق توفر في التصميم). الإقليم الثانوي الكامل = **OPS-004**؛ IaC سحابي كامل = **OPS-005**.
4. **التغليف:** [`deploy/k8s/`](../deploy/k8s/) بـKustomize (`base` + overlays `staging-ha` / `production-ha`). لا Helm في الموجة الأولى.
5. **ما يُنشر على k8s هنا:** Gateway + خدمات Web التطبيقية (stateless). PostgreSQL / RabbitMQ / Keycloak / MinIO تُستهلك كخدمات مُدارة أو خارج العنقود؛ HA لها = **OPS-002**.

## البدائل المرفوضة

| بديل | سبب الرفض |
|---|---|
| Docker Swarm | سطح تشغيل أضيق، اعتماد أقل في السوق، لا يغطي PDB/HPA/Ingress بنفس نضج المنظومة |
| Compose replicas فقط | لا multi-AZ ولا disruption budgets ولا Ingress controller موحّد |
| Helm من اليوم الأول | تعقيد XL إضافي؛ Kustomize كافٍ للموجة الأولى |

## العواقب

- مسار النشر الحالي (SHA tags، migrate job، health gate، rollback) من **DEP-001** يبقى مصدر صور GHCR؛ k8s يستهلك نفس الوسوم.
- إزالة الاعتماد على `container_name` في مسار HA (انظر overlay / سياسة التشغيل).
- Workers (ExportJob / PayrollRunJob) تعتمد lease/claim — HPA لا يضاعف الأثر الضار (موثّق في [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md)).
