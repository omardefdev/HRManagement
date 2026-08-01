# OPS-001 — قائمة تحقق HA (Kubernetes)

طوبولوجيا: [OPS_HA_TOPOLOGY.md](./OPS_HA_TOPOLOGY.md). سكربت مساعد: [`scripts/verify-k8s-ha.sh`](../scripts/verify-k8s-ha.sh).

يفترض عنقوداً بثلاث مناطق توفر (أو محاكاة labels) وoverlay `production-ha` مطبّق.

## قبل التشغيل

- [ ] صور SHA من DEP-001 (`IMAGE_TAG`) مضبوطة في المانيفستات
- [ ] Secrets/ConfigMaps للبيانات الخارجية جاهزة (HA البيانات = OPS-002)
- [ ] `kubectl kustomize deploy/k8s/overlays/production-ha` ينجح محلياً
- [ ] Job الترحيل اكتمل مرة واحدة قبل/مع أول rollout (ليس مع كل replica)

## توزيع المناطق وanti-affinity

- [ ] `kubectl get pods -n hrmanagement -o wide` — Gateway pods على hosts مختلفة
- [ ] تسميات `topology.kubernetes.io/zone` تظهر توزيعاً عبر ≥2 zones للـGateway عند توفر السعة
- [ ] لا يوجد `container_name` / اسم ثابت يمنع التوسع

## فقد replica واحدة

- [ ] حذف pod واحد من Gateway: `kubectl delete pod -n hrmanagement -l app.kubernetes.io/name=hr-gateway --field-selector=status.phase=Running` (واحد فقط)
- [ ] `/health/ready` عبر Ingress يبقى ناجحاً أثناء الاستبدال
- [ ] حذف pod من `hr-core-web` لا يسقط المسار عبر YARP Service DNS

## Rolling update

- [ ] تغيير annotation/image tag وطرح `kubectl rollout status deploy/hr-gateway -n hrmanagement`
- [ ] `maxUnavailable: 0` يحافظ على جاهزية خلال الـsurge
- [ ] جلسات/توجيه عبر Ingress لا تنقطع فوق SLO المعتمد للبوابة

## PDB وdrain

- [ ] `kubectl get pdb -n hrmanagement` يظهر budgets
- [ ] `kubectl drain <node> --ignore-daemonsets --delete-emptydir-data` يتوقف أو يحترم PDB عندما سيُكسر الحد الأدنى
- [ ] بعد uncordon، الـpods تعود موزّعة

## HPA وjobs

- [ ] `kubectl get hpa -n hrmanagement` — min يطابق replicas الافتراضية؛ هدف CPU 70%
- [ ] لا يوجد HPA على Job الترحيل
- [ ] تحت تحميل مكرر لـExportJob: صف DB + lease يمنع معالجة مزدوجة لنفس الـjob (PERF-004)

## Ingress / TLS / WAF

- [ ] Ingress يشير إلى Service `hr-gateway` فقط
- [ ] TLS مفعّل في الإنتاج (cert-manager أو سر يدوي)
- [ ] قواعد الحد الأدنى: ssl-redirect + rate limit (أو WAF سحابي أمامي) موثّقة

## إيقاف رشيق

- [ ] `ASPNETCORE_SHUTDOWNTIMEOUTSECONDS=30` و`terminationGracePeriodSeconds: 45` على Deployments
- [ ] عند SIGTERM: الجاهزية تُزال قبل القتل؛ لا قطع فجائي لطلبات قيد التنفيذ ضمن المهلة

## إغلاق البطاقة

- [ ] ADR + topology + verification موجودة
- [ ] الإشارة صريحة: **HA للبيانات = OPS-002** — بعد OPS-002 راجع أيضاً [OPS_DATA_HA_VERIFICATION.md](./OPS_DATA_HA_VERIFICATION.md)
