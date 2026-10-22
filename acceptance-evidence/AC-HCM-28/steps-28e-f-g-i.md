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