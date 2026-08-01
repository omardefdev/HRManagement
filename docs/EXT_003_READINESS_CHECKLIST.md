# EXT-003 — Readiness checklist

| Gate | Ready? | Note |
|---|---|---|
| Approved-run payment order binding | Yes | |
| Exactly-once / idempotency | Yes | |
| Totals recon + reject correction | Yes | |
| Manual file audit gate | Yes | |
| Cutoff / contingency contract | Yes | |
| Mock sandbox harness | Yes | |
| Live treasury / MOF API | No | **treasury-live** |
| Certified bank sandbox | No | **bank-sandbox-cert** |
| Postal channel live | No | **postal-channel-live** |
| Live national PKI signatures | No | **EXT-004** |
| UAT ReadyForSign-off | Yes | |

**Pack closure:** NOT-CLOSED.

The design pack is complete, but four gates in the table above are `No`, and each is a live
external dependency: the treasury API, a certified bank sandbox, the postal channel, and national
PKI signatures. Declaring closure while those are open reported a payment capability the system
does not have — no money can move through any of these channels today.

Per [بروتوكول التدقيق](AUDIT_PROTOCOL.md), a card closes only on live execution with saved
evidence and a named signature. This card closes when all four gates read `Yes` with a recorded
sandbox-to-settlement run behind each.
