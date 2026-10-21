# خطة التنفيذ التفصيلية — PLAN-05 (اعتمادية الرسائل)

تنفيذ [PLAN-05 — messaging-reliability](../PLAN-05-messaging-reliability.md) لإثبات **AC-15..AC-18**. يعتمد على [PLAN-00](PLAN-00-EXECUTION.md).

## المخرجات

- `ForwardAuthHandler` + `CorrelationIdConsumeFilter` (Serilog) + عقود `IEmployeeCreated` v4 / `ICardIssued` v2.
- `IntegrationMessageReceipts` على File.Storage + migration `INT002_IntegrationMessageReceipts`.
- سكربتات: `acceptance-correlation-trace.ps1`, `acceptance-inbox-duplicate.ps1`, `acceptance-rabbit-outbox-chaos.ps1`, `acceptance-consumer-catchup.ps1`.
- اختبارات: `FileStorageInboxIdempotencyTests`, `RabbitDownOutboxTests`, `Ac15`..`Ac18`.

## تحقق

```powershell
dotnet test src/Tests/HRManagement.UnitTests --filter ForwardAuthHandler
$env:RUN_TESTCONTAINERS = "true"
dotnet test src/Tests/HRManagement.IntegrationTests --filter "FileStorageInbox|RabbitDownOutbox"
$env:RUN_ACCEPTANCE = "true"
dotnet test src/Tests/HRManagement.AcceptanceTests --filter "Ac15|Ac16|Ac17|Ac18"
```

## DoD

- [x] CorrelationId عبر S2S + consume Serilog + عقود محدّثة
- [x] File.Storage inbox table + migration
- [x] 4 سكربتات acceptance + 4 اختبارات قبول + تكامل Postgres/Rabbit
- [x] `NFR_CHAOS_RUNBOOKS.md` مرتبط بـ AC-18
