# EXT-003 — Ack, reject, return

Settlement states: Submitted → Acked → Settled | Rejected | Returned.

| Event | Required behavior |
|---|---|
| ACK | Advance to Acked; await settle |
| NACK / REJECT | Open correction workflow (`treasury-reject-correction`); do not mark Settled |
| PARTIAL_REJECT | Correct rejected lines; resubmit only corrected subset with new idempotency key |
| RETURN | Reverse or hold; recon must show unpaid lines |

Silent overwrite of reject into Settled is forbidden. Fixture: `partial-reject-correct-pass`.

Out-of-order and delayed acks must still reconcile by `orderId` / `idempotencyKey` (fixtures `out-of-order-ack-pass`, `delay-ack-pass`).
