# PLAN-05 — اعتمادية الرسائل والأحداث (AC-15..AC-18)

**التغطية:** AC-15, AC-16, AC-17, AC-18. **الطبقة:** 2. **يعتمد على:** PLAN-00.

## الهدف

إثبات معرّف ارتباط واحد عبر الخدمات في رحلة التعيين، ومعالجة الرسالة المكررة مرة واحدة، وبقاء الرسالة في Outbox عند سقوط RabbitMQ ثم تسليمها لاحقاً دون فقد/تكرار، ولحاق الخدمة المستهلكة بعد استعادتها.

## الوضع الحالي (منفّذ / فجوة)

- منفّذ: [CorrelationIdMiddleware.cs](../../src/Shared/HRManagement.ServiceDefaults/CorrelationIdMiddleware.cs) + تمرير في النشر/الاستهلاك عبر [MassTransitExtensions.cs](../../src/Shared/HRManagement.ServiceDefaults/MassTransitExtensions.cs) و`CorrelationIdConsumeFilter`؛ Outbox على 8 خدمات (`AddPostgresOutbox`)؛ فلتر Inbox `InboxConsumeFilter` بمفتاح `(TenantId, MessageId, ConsumerType)`؛ إعادة محاولة + طوابير `_error`.
- فجوة **AC-15**: عميل S2S للملفات (`ForwardAuthHandler`) لا يمرّر `X-Correlation-ID`؛ فلتر الاستهلاك لا يدفع خاصية Serilog؛ `CorrelationId` غير موحّد في `IEmployeeCreated`/`ICardIssued`؛ لا سكربت رحلة يجمع المعرّف.
- فجوة **AC-16**: File.Storage بلا جدول `IntegrationMessageReceipts` (الفلتر بلا أثر).
- فجوة **AC-17**: اختبارات القتل محاكاة (SQLite/بلا إيقاف وسيط) — [OutboxAtomicityIntegrationTests](../../src/Tests/HRManagement.IntegrationTests/OutboxAtomicityIntegrationTests.cs)، `INT001_Outbox_KillRestart_NoLoss`.
- فجوة **AC-18**: لحاق المستهلك موثّق بـchaos/runbooks لا باختبار قبول مخصّص.

## تغييرات التنفيذ (مهام)

1. **AC-15:** تمرير `X-Correlation-ID` في `ForwardAuthHandler`؛ دفع خاصية Serilog في فلتر الاستهلاك؛ توحيد `CorrelationId` على `IEmployeeCreated`/`ICardIssued`؛ سكربت رحلة تعيين يجمع المعرّف من تدقيق البوابة وسجلات الخدمات ورؤوس Rabbit.
2. **AC-16:** إضافة `IntegrationMessageReceipts` لـFile.Storage؛ سكربت تسليم مزدوج (نفس `MessageId`) يثبت أثراً واحداً وإيصالاً واحداً.
3. **AC-17:** استخدام `docker-compose.chaos.yml`/إيقاف حاوية Rabbit فعلياً أثناء النشر؛ إثبات بقاء صف Outbox ثم تسليم واحد بعد الاستعادة.
4. **AC-18:** `docker stop` لمستهلك (Auth/Archive)، إكمال رحلة التعيين، ثم استعادته وإثبات تصريف الطابور واللحاق.

## سيناريوهات القبول (موجب + سالب)

| AC | موجب | سالب |
|----|------|------|
| AC-15 | معرّف واحد يظهر في العقد والهوية والملف والأرشيف والأحداث | لا انقطاع في السلسلة عبر الخدمات |
| AC-16 | إرسال الرسالة مرتين ⇒ أثر واحد | لا أثر مكرر (إيصال Inbox واحد) |
| AC-17 | إيقاف Rabbit أثناء عملية ⇒ الرسالة في Outbox، ثم تصل بعد التشغيل | لا فقد ولا تكرار |
| AC-18 | إيقاف مستهلك ⇒ باقي الخدمات تستمر، والمتوقف يلحق بعد العودة | لا فقد أحداث أثناء التوقف |

## منهج الاختبار

مجموعة `Ac15..Ac18` على الحزمة الحية؛ إعادة استخدام [InboxIdempotencyTests](../../src/Tests/HRManagement.IntegrationTests/)، `Qa003OutboxInboxRealInfraTests`، [docs/EVENT_PUBLISHER_INVENTORY.md](../EVENT_PUBLISHER_INVENTORY.md)، [docs/RABBITMQ_OPERATIONS.md](../RABBITMQ_OPERATIONS.md).

## مخرجات الأدلة

`acceptance-evidence/AC-15/`: تتبّع CorrelationId عبر الخدمات. `AC-16/`: عدّ الإيصالات والأثر. `AC-17/`: عدّ صفوف Outbox قبل/بعد التشغيل. `AC-18/`: عمق الطابور قبل/بعد الاستعادة.

## تعريف الإنجاز

معرّف واحد عبر الرحلة؛ معالجة مرة واحدة؛ لا فقد/تكرار عند سقوط الوسيط؛ لحاق المستهلك؛ AC-15..18 = `Pass`.
