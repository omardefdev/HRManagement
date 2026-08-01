# STR-005 — Journey matrix

Source: [`str-governance/governance-matrix.json`](str-governance/governance-matrix.json) — [ADR-STR-005](ADR-STR-005-program-governance-boards.md).

## DecisionRights

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| CriticalTypesMapped | Yes | program | decisionRights[] |
| OwnerNotReviewer | Yes | program | decision-log.json |

## BoardsAndAdr

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| FiveBoards | Yes | architecture | boards[] |
| AdrGate | Yes | architecture | adrRequired |

## ExceptionExpiry

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| DeclaredTemporary | Yes | program | exceptions[] |
| ExpiryEnforced | Yes | qa | str005-governance-validate |

## GoLiveMultiSeat

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| EngOnlyFails | Yes | sponsor | go-live/eng-only.json |
| MultiSeatPasses | Yes | sponsor | go-live/multi-seat.json |

Soft blockers: SYD-017 = Blocked; PGM-001 = NotStarted.
