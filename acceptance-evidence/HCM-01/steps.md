# HCM-01 acceptance evidence (Plan-6 complement)

| Scenario | Status |
|----------|--------|
| DSAR candidate masked export | AcHcm06CandidateDsarTests |
| Appeal blocks rank/offer | RecruitmentAppealWorkflowRules + Details UI gates |
| Committee session | SelectionCommitteesController + Index view |
| Portal rate-limit | PublicCareersRateLimitExtensions + 429 policy |
| Retention anonymize | CandidateApplicationLegalLifecycleService + CandidateLifecycleController |

Run: `acceptance-verify.ps1` checks DATA-CAND-PII, DATA-RET-CAND, DATA-APPEAL, DATA-COMMITTEE.
