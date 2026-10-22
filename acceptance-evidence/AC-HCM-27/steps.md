## 27a-c-d-h

Personas: central-a (maker), br1-mgr (offer approval step 1), a-management-approver (step 2).
1. A requisition is created, approved, given criteria and published through the UI; four HR applications are registered.
2. Test-only precondition (ranking is AC-HCM-26's scope): three applications become Ranked 1..3; the fourth is Assessed.
3. AC-27a (negative): an offer for the Assessed application is rejected.
4. AC-27c (negative): an offer for rank 1 with a salary above the Payroll grade maximum is rejected.
5. AC-27a (positive): an offer for rank 1 within the grade bounds → two-step RECRUITMENT-OFFER-APPROVAL → Issued.
6. AC-27d: HR records the candidate's decline with a reason → offer Declined, application OfferDeclined;
   the requisition and offer pages show «المرشح التالي بالجدارة» for rank 2; an offer for rank 2 is drafted without merit override.
7. AC-27h: the rank-2 draft is revoked as an administrative error → the application is Ranked again.

## 27b

Personas: central-a (maker), br1-mgr (step 1 «مراجعة HR»), a-management-approver (step 2), a-management-approver-2 (step 3).
1. Published requisition with ranks 1 and 2 (test-only ranking precondition).
2. AC-27b (negative): an offer for rank 2 without a merit override reason is rejected.
3. AC-27b (positive): an offer for rank 2 with IsMeritOverride and a 30+ character reason → RECRUITMENT-OFFER-OVERRIDE-APPROVAL.
4. br1-mgr approves step 1 and a-management-approver step 2; a-management-approver is refused on step 3 (distinct approvers).
5. a-management-approver-2 approves step 3 → Issued.

## 27e

1. Published requisition with rank 1; its offer is issued through the two-step workflow.
2. Test-only time travel: the offer's ExpiryDate is set to yesterday.
3. AC-27e (negative): accepting the expired offer is rejected (offer stays Issued).
4. The hr-core container is restarted so RecruitmentOfferExpiryJob runs its first pass (one minute after start).
5. AC-27e (positive): the offer becomes Expired and the application OfferDeclined («انتهاء صلاحية العرض»).

## 27f-g

1. Published requisition with ranks 1..3 and one Assessed application.
2. Rank 1's offer is issued (two-step workflow) and accepted; central-a hires the candidate through the Hire form.
3. AC-27f: ranks 2 and 3 become ReserveListed with ReserveUntil = today + 6 months; the Assessed application is Rejected.
4. Test-only time travel: rank 2's ReserveUntil is set to yesterday.
5. The hr-core container is restarted so RecruitmentReserveExpiryJob runs its first pass (two minutes after start).
6. AC-27g: rank 2 becomes Rejected («انتهاء صلاحية قائمة الاحتياط»); rank 3 stays ReserveListed.