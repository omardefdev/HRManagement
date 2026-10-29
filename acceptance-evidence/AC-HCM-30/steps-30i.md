Fixture (AcceptanceCandidateExperiencePatch): REQ-2022-A-RET1 cancelled four years before the reference date; pair 1
(recruitment application + linked citizen row AC30-RET-0001, one CV document) is due; pair 2 (AC30-RET-0002) has LegalHold.
Acceptance runs retention with Apply=true and the default 36 months (a four-year-old cancellation is due; no Months override).
1. a-directory-checker (hr-admin) opens «الاحتفاظ ببيانات المرشحين» and posts «تنفيذ الإخفاء» (CandidateLifecycle/apply).
   The job may already have anonymized pair 1 at startup: the end state is asserted either way.
2. AC-30i: pair 1 anonymized (both rows: AnonymizedAt, name and national id cleared), its CandidateDocument deleted and a
   Release intent for its file in FileReferenceOutbox; a finished, non-dry-run CandidateRetentionRuns row for the tenant.
3. AC-30i (negative): pair 2 untouched (names kept, AnonymizedAt null).