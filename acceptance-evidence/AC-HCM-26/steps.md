## 26a-e

Personas: central-a (hr-core, assessor 1), br1-mgr (hr-core, approval step 1, assessor 2), a-management-approver (step 2).
1. central-a creates a requisition on a free vacant BR1 position; br1-mgr and a-management-approver approve it.
2. AC-26a (negative): publishing without assessment criteria is rejected.
3. AC-26b (negative): a criteria version whose weights sum to 90 is rejected.
4. AC-26a (positive): criteria v1 (interview 40 / exam 30 / experience 30) is created and the requisition is published with a closing date.
5. An HR application is registered, screened, COI declared and shortlisted.
6. AC-26e (negative): an interview in the past is rejected. (positive): an interview is scheduled with a panel.
7. AC-26c (negative): Interview-stage scores before an attended interview are rejected.
8. Test-only time travel: the slot is moved one hour into the past; central-a completes it as Attended.
9. AC-26c (positive): central-a (80/70/90 → 80.00) and br1-mgr (60/50/70 → 60.00) score; final 70.00; status Assessed.
10. AC-26d (negative): a new criteria version on the locked version without a reason is rejected.
11. Rank 1 → Ranked. AC-26e (negative): scheduling for a Ranked application is rejected and the status is unchanged.

## 26f-g

1. Create, approve, define criteria and publish a requisition; register one HR application.
2. AC-26f (negative, UI): a second requisition on the same funded position is rejected.
3. AC-26f (negative, DB): a direct duplicate insert hits IX_RecruitmentRequisitions_Tenant_Position_Blocking.
4. AC-26g (positive): central-a cancels the published requisition with a reason → posting unpublished, application rejected, audit row.

## 26g-neg

1. Find a Published requisition in agency A that has an application in OfferAccepted or Hired (acceptance seed pipeline).
2. AC-26g (negative): central-a tries to cancel it with a valid reason → rejected, status unchanged.

## 26h

1. Publish two requisitions (positions P1, P2), each with one HR application.
2. Test-only precondition: insert an Issued offer on P2's application.
3. AC-26h (negative): freeze P2 via FundedPositions/Freeze → submit → central-a2 + a-management-approver approve → apply fails; P2 stays Vacant, requisition stays Published.
4. AC-26h (positive): freeze P1 the same way → P1 Frozen, requisition Cancelled («تجميد المنصب: …»), application Rejected.

## 26i

1. Publish a requisition with a closing date (≥ today + 7).
2. Test-only time travel: set its application closing date to yesterday.
3. Restart the hr-core container so RecruitmentIntakeCloseJob runs its first pass (one minute after start).
4. AC-26i: IntakeClosedAt is set, the posting is unpublished, the requisition stays Published,
   /careers shows «التقديم مغلق», and HR intake on the requisition is rejected.