# EXT-006 — Program adoption minutes (sandbox-cert production binding)

| Field | Value |
|---|---|
| Card | EXT-006 (card 78) |
| Date | 2026-07-30 |
| Decision | Create sandbox-cert matrix and bind into host startup/CI |
| Default live portal | `LiveSandboxPortalClaim=false` |

## Claim adoption rules

| Claim | Requires |
|---|---|
| `LiveSandboxPortalClaim=true` | EXT-006a + live-developer-portal Satisfied |
| Partner cert fleet live | EXT-006b + partner-cert-fleet-live |
| Production key rotation | EXT-006c |
| Live usage analytics | EXT-006d |
| MIG-007 EXT-006 Satisfied | EXT-006e |

## Signatories

| Role | Signature |
|---|---|
| API Product | _pending wet-ink_ |
| Integration Security | _pending wet-ink_ |

See [EXT_006_INDEPENDENT_REVIEW_WAIVER.md](EXT_006_INDEPENDENT_REVIEW_WAIVER.md).
