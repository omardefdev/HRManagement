# EXT-003 — Signatures and channel shapes

## Signatures

| Mode | Allowed in this pack? |
|---|---|
| Lab stub hash / role dual-control | Yes (`signatures.labStubAllowed`) |
| Live national PKI / HSM / timestamp | No — requires **EXT-004** Satisfied |

Claiming live PKI Done while EXT-004 is NotStarted is an EXCEPTION fail.

## Channel

| Shape | Lab |
|---|---|
| File `treasury-csv-v1` | FilePayrollDisbursementGateway → integration-outbox/treasury/ |
| API `treasury-submit-v1` | Mock only; live MOF API forbidden |

Ack codes: ACK, NACK. Return/reject: REJECT, RETURN, PARTIAL_REJECT.

See [EXT_003_MOCK_SANDBOX.md](EXT_003_MOCK_SANDBOX.md).
