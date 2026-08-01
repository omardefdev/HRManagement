# COM-001 — Multi-Channel Notifications

## Purpose

Provide durable, multi-channel notification delivery (in-app, email, SMS, push) with deduplication, delivery tracking, and user preference support. SignalR disconnection does not lose notifications; they are persisted and retried.

## Scope

- In-app / email / SMS / push channels
- Template-based localized content
- Recipient and tenant scoping
- User preferences and mandatory overrides
- Quiet hours
- Retry with deduplication (idempotency key)
- Delivery receipts
- Expiration and escalation
- History for each user

## Key Components

| Component | Location | Role |
|-----------|----------|------|
| `NotificationMessage` | `SharedKernel/Notifications/` | Durable notification entity with channel, status, idempotency |
| `INotificationService` | `ServiceDefaults/Notifications/` | Service contract for send / query / mark-read |
| `InAppNotificationService` | `ServiceDefaults/Notifications/` | In-memory implementation for dev/test; production uses persistent store + SignalR |

## Design Decisions

1. **Durable first** — messages are stored before delivery attempt; channel outage does not lose.
2. **Idempotency key** — prevents duplicate sends from the same domain event.
3. **Template codes** — content is localized at send time via L10N-001 infrastructure.
4. **Channel abstraction** — `INotificationService` implementations per channel; in-app is default.

## Acceptance Criteria

- SignalR disconnection does not lose notifications
- Duplicate event does not duplicate notification
- Delivery/failure is audited
- No sensitive PII in insecure channel
- Deep link re-authorizes
- User sees notification history

## Validation

- `scripts/com001-validate.ps1`
- `Com001NotificationTests`
