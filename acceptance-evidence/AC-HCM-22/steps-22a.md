1. central-a publishes a requisition on an AC33-POOL position (two-step approval) and enters an HR application.
2. Precondition (test-only SQL, ranking is AC-HCM-26): the application is Ranked 1 with COI declared.
3. central-a creates an offer at the G5 mid salary (Draft; application OfferExtended) and submits it.
4. br1-mgr approves step 1, a-management-approver step 2 → Issued with an expiry date.
5. central-a records the candidate's acceptance on their behalf with the signed PDF (File.Storage) →
   offer Accepted (AcceptedBy HrOnBehalf, evidence file id stored, audit AcceptOfferOnBehalf), application OfferAccepted.
Candidate self-acceptance on /careers/my with OTP is covered by AC-HCM-30 and by the E2E journey.