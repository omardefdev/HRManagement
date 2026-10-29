## 22a

1. central-a publishes a requisition on an AC33-POOL position (two-step approval) and enters an HR application.
2. Precondition (test-only SQL, ranking is AC-HCM-26): the application is Ranked 1 with COI declared.
3. central-a creates an offer at the G5 mid salary (Draft; application OfferExtended) and submits it.
4. br1-mgr approves step 1, a-management-approver step 2 → Issued with an expiry date.
5. central-a records the candidate's acceptance on their behalf with the signed PDF (File.Storage) →
   offer Accepted (AcceptedBy HrOnBehalf, evidence file id stored, audit AcceptOfferOnBehalf), application OfferAccepted.
Candidate self-acceptance on /careers/my with OTP is covered by AC-HCM-30 and by the E2E journey.

## 22b

Seed: OFF-2026-A-AC33-PEND (PendingApproval, step 1 of RECRUITMENT-OFFER-APPROVAL, created by central-a).
1. central-a opens the offer: «لا يمكن للمنشئ اعتماد عرضه الخاص.» and no approve form.
2. br1-mgr (control) gets the approve form.
3. Negative: central-a POSTs ApproveWorkflow directly → refused; status and step unchanged.

## 22c

Negative (seed): the session a0000001-0000-4000-8000-000000000925 has a chair only → submit refused, SubmittedAt stays empty.
Positive:
1. central-a publishes a requisition on an AC33-POOL position and enters an HR application;
   precondition (test-only SQL, assessment is AC-HCM-26): the application is Assessed.
2. central-a creates a Ranking session and adds the seeded roster (session a0000001-0000-4000-8000-000000000924):
   a chair + 3 members, all linked to no persona.
3. Every member votes Agree; decision Recommend with proposed rank 1; submit → SubmittedAt/LockedAt, RECRUITMENT-SELECTION-COMMITTEE.
4. br1-mgr approves steps 1 and 2, a-management-approver step 3 → ApprovedAt set.

## 22d

Design PLAN-28: HR files the appeal on the candidate's behalf → assigns an independent compliance reviewer
(by employee) → only that reviewer decides; the application follows the outcome.
1. central-a enters an application on REQ-2026-A-AC33-PUB, rejects it, files an appeal and assigns a-compliance.
2. a-compliance decides «Upheld» → appeal Decided, application back to UnderReview.
3. Negative: the seeded pending appeal a0000001-0000-4000-8000-000000000922 (assigned to a-compliance): a-compliance-2 gets 404 on its page,
   and a Decide POST from a-compliance-2 is refused (status stays Pending). a-compliance-2's token page is a second
   appeal assigned to them, which they then dismiss (clean-up, also a positive for an assigned reviewer).

## 22e

Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
1. central-a publishes a requisition on an AC33-POOL position; HR application ranked 1 (test-only SQL precondition).
2. central-a creates and submits an offer → PendingApproval, WorkflowInstanceId set (RECRUITMENT-OFFER-APPROVAL, step 1),
   not Issued; the offer page shows no «لم يُنشأ مسار اعتماد» warning.
3. Clean-up: br1-mgr rejects (offer back to Draft).
The non-lab case (IsLabScale=false + policy Off → PendingApproval without workflow, never Issued) is proven by
src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs (G-02, PLAN-25).