# MIG-004 — Journey matrix

Source: [`mig-match/match-matrix.json`](mig-match/match-matrix.json) — [ADR-MIG-004](ADR-MIG-004-identity-match-merge.md).

## ThresholdSignOff

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| ThresholdsDeclared | Yes | data-quality | thresholds |
| SignedPolicy | Yes | privacy-officer | threshold-signoff |

## NoHighRiskAutoMerge

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| PolicyBan | Yes | data-quality | thresholds.highRiskAutoMergeForbidden |
| HighRiskFail | Yes | qa | high-risk-auto-merge-fail |

## ReversibleMerge

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| AuditFields | Yes | data-quality | mergeAudit |
| RoundTrip | Yes | qa | merge-unmerge-roundtrip |

## ReviewPrivacy

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| TenantScoped | Yes | privacy-officer | reviewPrivacy |
| CrossTenantFail | Yes | qa | cross-tenant-review-fail |

## LabelledMetrics

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| LabelledPairs | Yes | qa | identity-pairs (≥20) |
| PrecisionRecall | Yes | qa | precision-recall-lab |

## AdversarialIdentity

| Step | Mandatory | Owner | Evidence |
|---|---|---|---|
| TwinsVariants | Yes | qa | twins-name-variants |

Soft blockers: agency-extract-access, live-labelled-metrics = Blocked; L10N-002 = NotStarted.
