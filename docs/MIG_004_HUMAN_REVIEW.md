# MIG-004 — Human review

| Field | Value |
|---|---|
| Queue | mig-identity-review |
| Roles | data-quality-reviewer, privacy-officer |
| SLA | 72 hours |
| Masking | PII masked by default |

High-risk and exception-queue cases require human decision before merge. Second reviewer required when `falsePositiveControls.secondReviewerForHighRisk` is true.

Reviewers see tenant-scoped, masked payloads only — see [MIG_004_REVIEW_PRIVACY.md](MIG_004_REVIEW_PRIVACY.md).
