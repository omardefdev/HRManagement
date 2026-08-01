# OPS-002 — قائمة تحقق HA لمستوى البيانات

طوبولوجيا: [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md). سكربت: [`scripts/verify-k8s-data-ha.sh`](../scripts/verify-k8s-data-ha.sh).  
النسخ الاحتياطي / PITR / استعادة خارج الموقع = **OPS-003** (خارج هذه القائمة).

يفترض عنقوداً بـ`hrmanagement-data` وoverlay `production-data-ha` (أو staging) مطبّق بعد `scripts/apply-cnpg-operator.sh`.

## قبل التشغيل

- [ ] CNPG operator جاهز (`cnpg-system`)
- [ ] Secrets الحقيقية استُبدلت (لا `replace-me`)
- [ ] `kubectl kustomize deploy/k8s/data/overlays/production-data-ha` ينجح
- [ ] تطبيقات `hrmanagement` تشير إلى pooler / rabbitmq / keycloak / minio في `hrmanagement-data`
- [ ] لا حسابات superuser/`minioadmin`/`guest` في ConfigMap التطبيقات

## PostgreSQL (CNPG)

- [ ] `kubectl -n hrmanagement-data get cluster hr-postgres` → instances جاهزة (3)
- [ ] خدمة `hr-postgres-pooler-rw` موجودة؛ التطبيقات تتصل بها مع `TargetSessionAttrs=read-write`
- [ ] قتل primary: `kubectl -n hrmanagement-data delete pod -l cnpg.io/instanceRole=primary` (أو حسب تسمية CNPG) → ترقية خلال RTO ≤ 60s
- [ ] التطبيقات تعيد الاتصال؛ لا split-brain (CNPG fencing)
- [ ] Migrate Job يتصل بالـRW فقط (ليس بكل replica)

## RabbitMQ

- [ ] 3 pods جاهزة؛ `rabbitmq-diagnostics cluster_status` يُظهر الأعضاء
- [ ] طوابير MassTransit من نوع quorum
- [ ] قتل عقدة واحدة → AMQP عبر Service `rabbitmq` يبقى متاحاً؛ confirms لا تفقد نشراً بعد ack
- [ ] PDB `maxUnavailable: 1` يمنع drain يكسر النصاب

## Keycloak

- [ ] إنتاج: 3 replicas؛ ISPN / `KC_CACHE_STACK=kubernetes`
- [ ] JDBC إلى `hr-postgres-pooler-rw` / `keycloak_db`
- [ ] حذف pod واحد → تسجيل الدخول عبر الـService يبقى ناجحاً
- [ ] health على منفذ management (9000)

## MinIO

- [ ] 4 pods distributed؛ Service `minio:9000`
- [ ] Job `minio-enable-versioning` اكتمل؛ versioning على buckets التطبيق
- [ ] إيقاف عقدة واحدة → قراءة/كتابة ضمن تحمّل erasure
- [ ] FileStorage يستخدم مفاتيح `minio-filestorage` وليس root

## ترقية rolling

- [ ] CNPG primary update strategy مجرّبة أو موثّقة
- [ ] RabbitMQ / Keycloak / MinIO rolling دون كسر PDB

## إغلاق البطاقة

- [ ] ADR + topology + verification موجودة
- [ ] الإشارة: **PITR / استعادة كاملة = OPS-003**
