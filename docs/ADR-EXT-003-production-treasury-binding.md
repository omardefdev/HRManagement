# ADR-EXT-003 — Treasury/payment production binding (supersedes pack-only closure)

| Field | Value |
|---|---|
| Status | Accepted |
| Card | EXT-003 (card 75) |
| Date | 2026-07-30 |
| Supersedes | Treasury/payment channel gate pack as insufficient for production |
| Dependencies | PAY-003, IAM-003, INT-003; EXT-004 soft (NotStarted) |

## Context

The prior pack published the treasury-payment matrix, approved-run / exactly-once / recon / reject / manual / cutoff contracts, and a shape validate harness, but hosts did not load the matrix at startup and CI did not gate Staging/Production `ExtTreasury` binding.

## Decision

| Topic | Choice |
|---|---|
| Source of truth | `docs/ext-treasury/treasury-payment-matrix.json` |
| Runtime | `ExtTreasuryOptions` + `IExtTreasuryProvider` + `ITreasuryPaymentClaimGuard` |
| Approved run | `approvedRunRequired` + Posted/Approved |
| Payment order | bindingToRunId + required fields including idempotencyKey |
| Exactly-once | idempotencyKeyRequired + duplicatePolicy=reject |
| Recon | totalsMustMatch + lineCountMustMatch |
| Reject | rejectCorrectionWorkflow.required |
| Manual files | unauditedForbidden |
| Cutoff | cutoffRequired |
| Live treasury | Default `LiveTreasuryClaim=false`; forbidden until EXT-003a–e |
| Soft blockers | PAY-003/IAM-003/INT-003 Satisfied; EXT-004 NotStarted; treasury-live / bank-sandbox-cert / postal-channel-live Blocked |
| Closure | **Done (production treasury-payment binding)** |

## Consequences

- Staging/Production fail startup on incomplete treasury policy, dishonest blockers, or live treasury claim.
- CI runs `ext003-treasury-payment-runtime-validate.ps1`.
- Live MOF/treasury/bank/postal connectors, ProcessPaymentBatch wire, SYR-011/PAY-004, and EXT-004 live PKI remain out of scope.
