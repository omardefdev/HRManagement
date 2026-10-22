## 28a-c-d

Personas: central-a (HR secretary), br1-mgr (steps 1–2), a-management-approver (step 3).
Seed: REQ-2026-A-CMT1 (RequiresCommitteeApproval), 3 Assessed candidates, session with chair + 3 members, unsubmitted.
1. AC-28c (negative): ranking a candidate before the committee is approved is refused (rank stays empty).
2. AC-28d: a member declares a conflict with candidate 1 → their vote on that candidate is removed; their votes on the others stay.
3. Every eligible member votes Agree; decisions: candidates 1 and 2 Recommend (ranks 1, 2), candidate 3 Reserve (rank 3).
4. AC-28a: submit → session locked (SubmittedAt/LockedAt); adding a member after submit is refused.
5. AC-28a: br1-mgr approves steps 1–2, a-management-approver step 3 → ApprovedAt; the minutes PDF downloads.
6. AC-28c (negative): a rank different from the proposed rank without a reason is refused.
7. AC-28c (positive): «تطبيق ترتيب اللجنة» assigns the proposed ranks 1..3.

## 28b

Persona: central-a creates a new session on REQ-2026-A-CMT1 (CreatedBy = central-a) and acts as HR secretary.
1. Chair + 2 members are added from the employee picker; adding the same employee again is refused.
2. Every member votes Agree on every Assessed/Ranked candidate; each gets a Recommend/Reserve decision; submit.
3. AC-28b (negative): central-a, the session creator, approves step 1 → refused (creator never approves); the step stays 1.

## 28e-f-g-i

Personas: central-a (HR: rejects, files, assigns), a-compliance (assigned reviewer), a-compliance-2 (not assigned).
1. A requisition is created, approved and published through the UI; three HR applications; test-only precondition: ranks 1..3.
   A committee session is created on it with one member (employee E).
2. AC-28e: central-a rejects rank 3 with a note → RejectionKind SelectionDecision, RejectedAt; files an appeal on the candidate's
   behalf (no reviewer); assigning E (committee member) is refused; assigning a-compliance's employee starts RECRUITMENT-APPEAL-REVIEW.
3. AC-28e: a-compliance opens /ui/hr-core/RecruitmentAppeals and the work inbox (compliance-only access) but not /ui/hr-core/Employees;
   a-compliance-2 gets 404 on appeal 1. Rank 2 is rejected and appealed (appeal 2) and assigned to a-compliance-2, who then
   tries to decide appeal 1 → refused. a-compliance decides appeal 1 «Upheld» → UnderReview, appeal Decided, rejection cleared.
4. AC-28f: appeal 2 is reassigned to a-compliance (the running review is cancelled); a-compliance asks for information;
   central-a resubmits; a-compliance decides «Dismissed» → stays Rejected with the original kind/date and the rationale stored.
5. AC-28g (negative): a new application rejected 15 days ago (test-only time travel on RejectedAt) cannot be appealed.
6. AC-28i (negative): the legacy «استئناف (مسار قديم)» card is gone and POST RecruitmentApplications/ReopenAppeal → 404.

## 28h

Personas: central-a (maker), br1-mgr + a-management-approver (offer approval), a-compliance (reviewer).
1. Published requisition with ranks 1 and 2 (test-only ranking precondition).
2. Rank 2 is rejected and appealed (pending). Offers stay allowed: rank 1's offer is issued (two-step workflow) and accepted.
3. AC-28h (negative): central-a submits the Hire form for rank 1 → refused; the application stays OfferAccepted.
4. a-compliance (assigned) dismisses the appeal → the same Hire form now hires rank 1.