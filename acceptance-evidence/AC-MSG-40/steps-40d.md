1. a-contracts-maker creates a leave through the UI; a-contracts-checker opens it.
2. RabbitMQ stopped; the approval commits the state and the OutboxMessage row.
3. RabbitMQ started: the outbox drains and Records.Archive records exactly one more receipt (no duplicate, no state without its event).
AC-16, AC-17 and AC-18 are rerun live next to this scenario.