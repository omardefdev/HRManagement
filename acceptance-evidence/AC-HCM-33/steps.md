## 33c

Personas: central-a (HR, creator), br1-mgr (step 1, hr-core, RIF), a-management-approver (step 2), an anonymous citizen.
1. central-a: requisition on a free AC33-POOL position (RIF, G5), criteria v1, submit → PendingApproval + RECRUITMENT-REQUISITION-APPROVAL.
   Refusal: a-management-approver gets no approve form at step 1 («هذه الخطوة تتطلب دور «hr-core».»; server-side role check = G-04, PLAN-25).
2. br1-mgr step 1 → a-management-approver step 2 → Approved → publish with a closing date 10 days ahead.
3. Refusal: with the closing date moved to yesterday (test-only SQL, restored) the citizen's application is refused.
   Citizen applies on /careers with profile, consent and a PDF CV (File.Storage + ClamAV) → CitizenApplication + RecruitmentApplication.
4. central-a: screen → COI → shortlist → interview (scheduled ahead, moved into the past by test-only SQL as in AC-HCM-26) →
   attended → scores by central-a and br1-mgr → Assessed → rank 1 → Ranked (no committee: RequiresCommitteeApproval=false).
   Refusal: an offer for the not-yet-ranked application is refused.
5. central-a: offer at the G5 mid salary → submit → br1-mgr → a-management-approver → Issued. Refusal: the creator's own approval is refused.
   Refusal: hire before acceptance is refused.
6. Citizen: OTP sign-in (code read from the FileDrop message); refusal: a wrong code opens no session; accepts on /careers/my.
7. central-a: hire as a new employee (the citizen's national id).
8. HR.Contracts: Appointment transaction for the employee and a contract with the offer grade and salary; the X-Correlation-ID of the
   run is echoed by the gateway on the hire and appears in the HR.Core and HR.Contracts logs (Ac15 pattern).

## Executed

- 15:26:05 1 requisition
  - after submit: PendingApproval|8da6e3eb-0106-4c03-bd83-7f81d74266cc
- 15:26:08 2 approval and publication
- 15:26:09 3 public application
- 15:26:12 4 selection
  - test-only SQL: interview slot moved one hour into the past (as AC-HCM-26)
- 15:26:16 5 offer
- 15:26:20 6 candidate acceptance
- 15:26:22 7 hire
- 15:26:24 8 contracts and correlation
- 15:26:26 done
