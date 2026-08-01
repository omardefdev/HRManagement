# Idempotency Strategy (Integration Consumers)

> **INT-002 canonical doc:** [INBOX_IDEMPOTENCY.md](./INBOX_IDEMPOTENCY.md) — unified filter, tenant key, atomic TX, retry/replay/retention.

## Two-tier model

| Tier | When | Mechanism | Location |
|------|------|-----------|----------|
| **A — Message inbox** | Creates rows, disables accounts, irreversible side effects | `IntegrationMessageReceipts` unique `(TenantId, MessageId, ConsumerType)` via `InboxConsumeFilter` | Per-service PostgreSQL DB |
| **B — Business-key marker** | Natural unique business key exists | Stable string in `AdjustmentReason`, `ReferenceNumber`, ticket description, etc. | [`ConsumerMessageMarkers.cs`](../src/Shared/HRManagement.ServiceDefaults/ConsumerMessageMarkers.cs) |

## Shared helpers

- Entity: `HRManagement.SharedKernel.Entities.IntegrationMessageReceipt`
- Filter: `InboxConsumeFilter<T>` registered in `MassTransitExtensions.AddServiceMassTransit`
- Extensions: `StageReceipt`, `HasReceiptAsync`, `ExecuteInInboxTransactionAsync`, `RequireMessageId`
- Legacy `ShouldSkipAsync` / `TryRecordAsync` — prefer the filter; kept for transitional callers

## Per-consumer mapping

| Consumer | Tier | Key |
|----------|------|-----|
| All consumers on services with receipt table | A (filter) | Tenant + MessageId + endpoint name |
| Payroll leave/advance/contract/transfer | A + B | Filter + `ConsumerMessageMarkers.*` |
| Records.Archive | A + B | Filter + `ReferenceNumber` |
| HR.Core leave/advance approved | A + B | Filter + status history reason |
| Auth.Directory / IT / Health / Admin | A (+ B where marked) | Filter (+ markers) |

## Retry interaction

MassTransit retries transient faults (default 3 × 5s). Permanent validation/business exceptions are **ignored** by retry and go to `*_error`. Inbox makes redelivery safe.

## Outbox vs inbox

| Pattern | Purpose |
|---------|---------|
| **Outbox** (publisher, INT-001) | Message published only after DB commit |
| **Inbox** (consumer, INT-002) | Same message not processed twice per tenant+consumer |

Both are required for production-safe critical flows.
