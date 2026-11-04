# PLAN-40: atomic event publishing

Every publish is written to the outbox in the transaction that commits its state, in 13 services.

- Class A (10), class B (39) and class C (56) sites fixed; consumers and sagas publish through the consumer outbox; outboxes added to five services; Training receipts.
- Guards: `OutboxPublishOrderingArchitectureTests` (R1-R4) and `outbox_unsaved_messages_total` with `UnsavedOutboxGuardMiddleware`.
- Replay of events lost before the fix: `scripts/msg040-replay-lost-events.ps1`, dry run by default.
- The live run also fixed: `IPolicyPublished` dropped by the broker, transient failures sent to `_error`, `/metrics` closed to the outside, state-history rows failing in consumers.

Verification: AC-MSG-40a..h 8/8, AC-16/17/18 3/3, AcHcm29 3/3, prod-like journey 1/1. Demo server on `f44e52cf58be`, server checks green, replay dry run 0 lost events (not applied).

Open: review finding M4 (non-asset workflow handlers run after the instance commits). Report: `reports/plan40-final-report.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
