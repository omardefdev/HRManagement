1. Requisition (law50) on a free graded BR1 position, ranks 1..2 (test-only SQL rank precondition).
2. Rank 1's offer (Payroll grade mid salary) → two-step approval → Issued. AC-29f: DocumentSha256 stored; GET OfferLetters/Document → PDF.
3. Accepted. Test-only SQL: the application gets a national id X.
4. AC-29b: Hire CreateNew with a seeded employee's national id → form error «الرقم الوطني مسجَّل لموظف قائم…», no hire.
5. AC-29c: Hire CreateNew with Y ≠ X and no reason → form error (mismatch), no hire.
6. AC-29a: Hire CreateNew with X → Hired; HR.Contracts pending contract of type ACC-LAW50-PERM with the offer grade, salary, law50, Permanent, source offer.
7. AC-29h: the Appointment transaction carries RecruitmentApplicationId / RequisitionId / OfferLetterId / ExpectedStartDate.
8. AC-29d: test-only SQL signature + commencement on the contract; a-contracts-checker activates → Payroll profile with the offer salary and the grade of the scale in force.
9. AC-29g: test-only SQL moves the transaction to Executing and completes the non-PLAN-29 items; Step4Complete is refused while «إصدار قرار التعيين» is open; the decision is issued (DEC-APPT-…); Step4Complete then succeeds.