# MIG-004 — Fraud indicators (pre-merge)

Migration-wave indicators that force HumanReview (not continuous FRD-001 ops):

| Id | Effect |
|---|---|
| ghost-employee-signal | forcesReview |
| cross-agency-same-nid | forcesReview |
| payroll-without-hire | forcesReview |

Presence of any indicator forbids AutoMerge regardless of score. Continuous post-go-live fraud controls remain **FRD-001** (out of scope).
