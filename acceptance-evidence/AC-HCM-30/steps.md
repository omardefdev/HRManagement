## 30a-b-h

1. AC-30a: a citizen applies on the seeded requisition REQ-2026-A-CX01 through the gateway (/careers/{posting}) with email +
   notification consent (documents: AC-30c) → RecruitmentApplication Submitted at once, IntakeChannel Public, linked to the
   citizen row; central-a sees it in RecruitmentApplications?RequisitionId=….
2. AC-30b: central-a rejects with a note. The citizen signs in on /careers/my (reference + national id → one-time code read
   from the FileDrop file of key otp:{challengeId}:Email) → «غير مقبول» and «يمكنك الاستئناف حتى …»; /careers/track shows
   «غير مقبول».
3. AC-30h: the candidate files the appeal from /careers/my → RecruitmentAppeals row FiledBy Candidate, Pending; listed for HR.

## 30c

1. AC-30c: an apply with a CV named malware.exe (MZ bytes) → the form is shown again, nothing is saved.
2. AC-30c: an apply with a 5 MB + 1 byte PDF → refused, nothing is saved.
   (Steps 1–2 are refused before any upload, so they run without File.Storage.)
3. AC-30c: a citizen applies with a CV and a certificate (PDF) → both listed on HR Details (CandidateDocuments Cv, Certificate)
   and «سليم» after the ClamAV scan; the CV downloads through the HR proxy (%PDF). Skipped when the apply page reports
   «تعذّر رفع المستندات حالياً…» (File.Storage not in the acceptance stack).
4. AC-30c: an apply with eicar.pdf (minimal PDF + EICAR string, the AC-28 pattern) is accepted; File.Storage marks the file
   Infected/Quarantined; HR Details shows «محجور — غير متاح» without a download link and the HR download proxy refuses it.

## 30d-e

1. AC-30d: a citizen applies with a diploma (DIP < BACH) and 1 year of experience (< 2) → flags BelowMinQualification and
   BelowMinExperience; HR Details shows «دون المؤهل المطلوب»; the application is screened (UnderReview);
   shortlisting without a reason is refused; with a reason → Shortlisted, EligibilityOverrideReason + audit EligibilityOverride.
2. AC-30e: central-a schedules an interview → event InterviewScheduled → exactly one FileDrop message (key {eventId}:Email)
   whose subject and body hold no national id.
3. AC-30e (negative): test-only SQL clears NotificationsPlannedAt on that event (replan) and the dispatcher runs again →
   still one CandidateNotification for the key, the FileDrop file is not rewritten.
4. AC-30b (negative): the shortlisted/interviewed candidate's /careers/my shows «مدعو للمقابلة» and no hire step («التعيين»).

## 30f-g

1. A requisition (law50) on a free graded BR1 position is created, approved and published through the UI (as AC-HCM-29).
2. Two citizens apply through /careers; test-only SQL ranks them 1 and 2 (AC-HCM-26's scope).
3. Rank 1's offer is created, approved (two steps) → Issued.
4. AC-30g: rank 1 signs in with the one-time code (FileDrop) and declines with a reason → OfferDeclined, DeclinedBy Candidate,
   application OfferDeclined; HR sees «المرشح التالي بالجدارة» for rank 2 (Recruitment:AutoDraftNextOffer is off in
   acceptance; with it on a Draft offer for rank 2 is accepted instead).
5. Rank 2's offer is created and approved → Issued. AC-30f: rank 2 signs in, downloads the offer letter (%PDF) and accepts
   (acknowledgement + full name) → offer Accepted, AcceptedBy Candidate, application OfferAccepted.

## 30f-negative

1. Two citizens (L and P) apply on REQ-2026-A-CX01.
2. AC-30f: L requests a code; five wrong codes → «رمز التحقق غير صحيح…» then locked (PortalLockedUntil, audit
   CandidatePortalLocked); the real code (FileDrop) is then refused with «تم إيقاف الدخول مؤقتاً…»; no careers_session.
3. AC-30f: L's reference with P's national id → the same neutral message, no code is created for either, no session;
   /careers/my redirects to the sign-in page.

## 30h-negative

1. A citizen applies; central-a rejects with a note; test-only SQL moves RejectedAt 15 days back (as AC-28g).
2. AC-30h: /careers/my shows «انتهت مدة الاستئناف» and no «تقديم الاستئناف» button; a POST to /careers/my/appeal is refused
   (the page again, no RecruitmentAppeals row).

## 30i

Fixture (AcceptanceCandidateExperiencePatch): REQ-2022-A-RET1 cancelled four years before the reference date; pair 1
(recruitment application + linked citizen row AC30-RET-0001, one CV document) is due; pair 2 (AC30-RET-0002) has LegalHold.
Acceptance runs retention with Apply=true and the default 36 months (a four-year-old cancellation is due; no Months override).
1. a-directory-checker (hr-admin) opens «الاحتفاظ ببيانات المرشحين» and posts «تنفيذ الإخفاء» (CandidateLifecycle/apply).
   The job may already have anonymized pair 1 at startup: the end state is asserted either way.
2. AC-30i: pair 1 anonymized (both rows: AnonymizedAt, name and national id cleared), its CandidateDocument deleted and a
   Release intent for its file in FileReferenceOutbox; a finished, non-dry-run CandidateRetentionRuns row for the tenant.
3. AC-30i (negative): pair 2 untouched (names kept, AnonymizedAt null).

## 30j

Fixture: two unlinked citizen rows on REQ-2026-A-CX01 (AC30-BF-0001 Received, AC30-BF-0002 UnderReview; recent dates) and the
seed pack's unlinked internal application (UnderReview) on the BR1 requisition.
1. scripts/hcm030-backfill-unified-intake.ps1 -DryRun -DockerContainer <acceptance postgresql> → reports, writes nothing.
2. AC-30j: -Apply → every unlinked requisition-linked row gets a RecruitmentApplication (Public/Internal) with the mapped
   status (Received → Submitted, UnderReview → UnderReview; Rejected for a closed/cancelled requisition) and the link.
3. AC-30j (negative): a second -Apply links nothing (Total linked: 0) and inserts no RecruitmentApplications.