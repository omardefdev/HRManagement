# مراقبة RabbitMQ (MassTransit)

## ما تراقبه

- طول الطوابير وعدد الرسائل غير المُعالَجة.
- معدل إعادة التسليم والرسائل في **dead-letter** (طوابير `_error` الافتراضية لـ MassTransit على RabbitMQ).
- سجلات التطبيق (Serilog) مع `CorrelationId` عند تتبع طلب HTTP مرتبط بنشر رسالة.
- أخطاء المستهلك بعد استنفاد Retry — `MassTransitFaultLogger` (`ConsumeFault`).

## إعدادات التطبيق

مفاتيح `RabbitMQ:Host`، `Port`، `VirtualHost`، `Username`، `Password` في `appsettings` أو متغيرات البيئة (`RabbitMQ__Host`، إلخ).

### HA / quorum / confirms (OPS-002)

على Kubernetes البيانات (`deploy/k8s/data/`): عنقود **3 عقد** مع peer discovery. من MassTransit:

```json
"RabbitMQ": {
  "UseQuorumQueues": true,
  "PublisherConfirmation": true
}
```

- `UseQuorumQueues` (افتراضي `true`): طوابير الاستقبال تُنشأ كـ`x-queue-type=quorum` عبر `SetQuorumQueue()`.
- `PublisherConfirmation` (افتراضي `true`): النشر لا يُعتبر ناجحاً حتى ack من الوسيط.
- فقد عقدة واحدة من 3: الطوابير quorum تبقى متاحة؛ `cluster_partition_handling=pause_minority` يقلل split-brain.
- HPA لتطبيقات OPS-001 لا يغيّر نوع الطابور؛ Inbox/Outbox + lease يمنعان الأثر المزدوج.
- Compose أحادي العقدة: quorum ما زال يعمل (نسخة واحدة فعلياً). راجع [OPS_DATA_HA_TOPOLOGY.md](./OPS_DATA_HA_TOPOLOGY.md).

### إعادة المحاولة (Retry)

في [`MassTransitExtensions.cs`](../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs):

```json
"MassTransit": {
  "RetryCount": 3,
  "RetryIntervalSeconds": 5,
  "RetryUseJitter": true
}
```

- **NFR-002:** عند `RetryUseJitter=true` (الافتراضي) تُستخدم `Exponential` (تأخيرات شبه عشوائية ضمن حد أدنى/أقصى) بدل `Interval` الثابت.
- عيّن `RetryUseJitter=false` لإعادة الفترات الثابتة (مقارنة مخبرية).
- `RetryCount: 0` يعطّل `UseMessageRetry`.
- استثناءات دائمة (`InvalidOperationException` / `Argument*` / `UnauthorizedAccessException`) تُتجاهل من الـretry وتنتقل مباشرة إلى `*_error` (INT-002).
- بعد استنفاد المحاولات للعابرة، تنتقل الرسالة الفاشلة إلى صف خطأ MassTransit (`*_error`) على RabbitMQ.

### Dead-letter (DLQ)

- MassTransit ينشئ تلقائياً `{queue-name}_error` على RabbitMQ.
- راقب عمق هذه الطوابير في واجهة إدارة RabbitMQ.
- **إعادة التشغيل (INT-002):** استخدم `ErrorQueueReplay` مع فرض مطابقة رأس `X-Tenant-ID`.
- **INT-003 process sagas:** timeouts تنتقل إلى `ProcessInterventions` وليس إلى نجاح صامت؛ راجع [PROCESS_SAGAS.md](./PROCESS_SAGAS.md).
- راجع [INBOX_IDEMPOTENCY.md](./INBOX_IDEMPOTENCY.md).

### Idempotency

| نمط | أين |
|-----|-----|
| Inbox موحّد `(TenantId, MessageId, ConsumerType)` عبر `InboxConsumeFilter` | كل خدمة تستهلك وتملك جدول `IntegrationMessageReceipts` |
| `AdjustmentReason` / markers (Tier-B) | Payroll، Archive، IT، Compliance — دفاع إضافي |
| Retention | `Inbox:RetentionDays` (افتراضي 90) |

راجع [INBOX_IDEMPOTENCY.md](./INBOX_IDEMPOTENCY.md) و [IDEMPOTENCY_STRATEGY.md](./IDEMPOTENCY_STRATEGY.md).

### Outbox (ناشرون)

HR.Contracts، HR.Core، Payroll، Movements، Health، IDCard، Compliance، Training — MassTransit EF outbox (`AddPostgresOutbox`). النشر يُؤجَّل حتى نجاح `SaveChangesAsync` (INT-001).

## تعطيل فحص RabbitMQ في الصحة

في أي خدمة:

```json
"HealthChecks": {
  "IncludeRabbitMq": false
}
```

## واجهات الإدارة

استخدم واجهة إدارة RabbitMQ (أو Prometheus + exporter) حسب بيئة التشغيل لديك. راقب طوابير `_error` بعد تفعيل Retry.

## الصورة والمستخدمون بأقل صلاحية (PLAN-38)

- **الصورة `hr-rabbitmq`** ([docker/rabbitmq/Dockerfile](../docker/rabbitmq/Dockerfile)): `rabbitmq:3.13-management-alpine` مثبتة
  بالـ digest، مع إضافة `rabbitmq_delayed_message_exchange` 3.13.0 (SHA-256 مثبت في الـ Dockerfile ويُتحقق منه عند البناء)،
  مفعّلة دون اتصال. تحتاجها `UseDelayedMessageScheduler` لمهل الـ sagas: كل `Schedule(...)` يعلن تبادلاً
  `<طابور الـ saga>_delay` من نوع `x-delayed-message`. تُبنى محلياً في التطوير والقبول، وعلى الخادم مع بقية الصور
  (`build-on-host`)، ويسحبها ملف الصور `${HRM_IMAGE_REPO}/hr-rabbitmq:${IMAGE_TAG}`.
- **عقدة ثابتة**: `hostname: rabbitmq` و`RABBITMQ_NODENAME=rabbit@rabbitmq` وvolume مسمّى `rabbitmq_data`، فلا تضيع
  رسالة دائمة ولا طابور quorum عند `docker compose up -d --force-recreate rabbitmq` (AC-OPS-38f).
- **المستخدمون** ([deploy/bootstrap/rabbitmq-users.json](../deploy/bootstrap/rabbitmq-users.json)، بلا أسرار): مستخدم
  `mq_*` لكل خدمة لها ناقل (14؛ البوابة بلا ناقل). صلاحية `configure` مشتقة من جرد حي
  ([reports/plan38-rabbit-topology.json](../reports/plan38-rabbit-topology.json)):
  `^(<prefix>-.*|HRManagement\.MessageContracts\.(Events|Commands):.*|MassTransit:Fault.*|[^-]+_bus_[^-]+)$`
  (و`HR\.Contracts\.Infrastructure\.Sagas:.*` لـ HR.Contracts). البادئة = اسم مشروع `.Web` بأحرف صغيرة وشرطات
  (`MassTransitExtensions.EndpointPrefixFor`). `read` = تعبير `configure` نفسه (لا يستهلك مستخدم طوابير خدمة أخرى ولا يفرغها)، و`write` = `.*` (النشر والإرسال). لا يستطيع مستخدم إعلان طابور خدمة أخرى ولا قراءته
  (`ACCESS_REFUSED`، AC-OPS-38c). ينشئهم `hrm-bootstrap` (الخطوة 6) عبر واجهة الإدارة الداخلية ويضع كلمات مرورهم عند كل نشر.
- **الوضعان (D-38-2)**: `HRM_MQ_POSTURE=least-privilege` افتراضياً (حارس الإنتاج يرفض أي مستخدم غير `mq_*`)؛
  `HRM_MQ_POSTURE=admin` يضيف `docker-compose.posture-mq-admin.yml` للرجوع إلى الحساب الإداري فقط.
- **فحص**: `docker exec <rabbitmq> rabbitmqctl list_connections user` يجب أن يظهر `mq_*` فقط؛
  `rabbitmq-plugins list -e rabbitmq_delayed_message_exchange` يظهرها `[E*]`.
- **ملاحظة**: `ScheduleSend` إلى عنوان مختصر `queue:<name>` يعلن طابوراً classic فيتعارض مع طابور الاستقبال quorum
  (`PRECONDITION_FAILED`) ويعيد المحاولة بلا نهاية؛ الـ sagas تجدول إلى عنوان إدخالها هي، وهذا مختبر
  (`Ops038RabbitLeastPrivilegeTests`). لا تستخدم العنوان المختصر للجدولة.
