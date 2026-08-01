# Inbox و Idempotency الموحدان (INT-002)

## الهدف

منع معالجة نفس رسالة التكامل أكثر من مرة لكل `(TenantId, MessageId, ConsumerType)` مع ضمان أن أثر الأعمال وإيصال الصندوق الوارد يُحفظان في **معاملة واحدة**.

## المفتاح الفريد

| حقل | المعنى |
|-----|--------|
| `TenantId` | عزل المستأجر (لا replay عبر الجهات) |
| `MessageId` | معرّف MassTransit للرسالة — **مطلوب** (فشل مغلق إن غاب) |
| `ConsumerType` | اسم نقطة الاستقبال (مسار الـ endpoint بعد `ConfigureEndpoints`) |

فهرس: `IX_IntegrationMessageReceipts_Tenant_Message_Consumer` (unique).

## الفلتر الموحّد

`InboxConsumeFilter<T>` مسجّل في `AddServiceMassTransit` عبر `UseConsumeFilter`:

1. `BeginTransaction`
2. إن وُجد إيصال → skip + commit فارغ (`inbox_duplicate_skip`)
3. `await next` (المستهلك يكتب عبر نفس `DbContext`)
4. `StageReceipt` + `SaveChanges` + `Commit` (`inbox_processed`)
5. انتهاك فريد → Rollback كامل للأثر + الإيصال (`inbox_unique_conflict`)

الخدمات التي لا تملك جدول الإيصالات تمرّر الفلتر دون تغيير.

## Tier-B (يبقى)

المؤشرات التجارية في `ConsumerMessageMarkers` دفاع إضافي ولا تُحذف في INT-002.

## Retry / Poison / Replay

- **عابرة:** مهلة / شبكة / concurrency → `UseMessageRetry` (افتراضي 3×5ث).
- **دائمة:** `InvalidOperationException` / `ArgumentException` / `UnauthorizedAccessException` → `Ignore` → طابور `{queue}_error`.
- **Replay:** `ErrorQueueReplay.EnsureTenantMatch` يرفض رأس مستأجر غير مطابق. لا بناء DLQ بديل — الاعتماد على MassTransit `*_error`.

```csharp
ErrorQueueReplay.EnsureTenantMatch(expectedTenantId, originalHeaders);
await ErrorQueueReplay.ReplayAsync(publishEndpoint, message, tenantId, originalHeaders);
```

## Retention

`InboxRetentionHostedService` يحذف إيصالات أقدم من `Inbox:RetentionDays` (افتراضي 90) كل `Inbox:RetentionInterval`.

```json
"Inbox": { "RetentionDays": 90, "EnableRetention": true }
```

## تسجيل المستهلكين

`consumerAssembly:` في `AddServiceMassTransit` → `AddConsumers(assembly)` + `ConsumerRegistrationHostedService` يفشل الإقلاع عند عدم تطابق الاكتشاف والتسجيل.

## المقاييس (OpenTelemetry Meter `HRManagement.Inbox`)

| Counter | المعنى |
|---------|--------|
| `inbox_duplicate_skip` | تكرار متوقع |
| `inbox_processed` | معالجة ناجحة |
| `inbox_unique_conflict` | سباق فريد → rollback |
| `consumer_fault_permanent` | (مخصّص للتوسعة) |
| `consumer_retry` | (مخصّص للتوسعة) |

## علاقة Outbox (INT-001) و PAY-003

| نمط | دور |
|-----|-----|
| Outbox | نشر ذري مع حالة الناشر |
| Inbox | استهلاك ذري بدون أثر مزدوج |
| PAY-003 محلي | `IX_SalaryLines_Run_Employee` + قفل job — دفاع مكمّل داخل الرواتب، لا يستبدل Inbox |

## اختبارات البوابة

`InboxIdempotencyTests` / `IdempotentConsumerTests`: duplicate، concurrent، crash/rollback، عزل مستأجر، MessageId مفقود، replay، startup registration.
