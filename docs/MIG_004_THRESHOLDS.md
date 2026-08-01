# MIG-004 — Thresholds

Lab policy version `2026.07.30.1` (signed in matrix `signing`).

| Knob | Value | Meaning |
|---|---|---|
| autoMergeMaxRisk | Low | Auto-merge only at Low risk |
| reviewMinScore | 0.72 | Score band for HumanReview |
| rejectBelowScore | 0.40 | Below → Reject |
| precisionTarget | 0.95 | Lab labelled minimum |
| recallTarget | 0.90 | Lab labelled minimum |
| highRiskAutoMergeForbidden | true | High risk never auto-merges |

Signing requires distinct `signedByRole` and `reviewerRole` plus non-empty `contentHash`.

Live agency precision/recall remains **Blocked** (`live-labelled-metrics`).
