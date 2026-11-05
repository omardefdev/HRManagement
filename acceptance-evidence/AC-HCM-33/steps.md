## 33a

AC-HCM-33a — acceptance without data shows Skipped, never Passed.

Observed 2026-10-29 (PLAN-33 P1): `dotnet test src/Tests/HRManagement.AcceptanceTests` without RUN_ACCEPTANCE → 23 passed (static, stack-free tests only), 90 skipped, 0 failed: no live scenario passes without the stack. Guarded by AcceptanceSkipPatternArchitectureTests (no WriteResult("Skip"), no result+return, no raw Skip.If, every Ac* method asserts) and AcceptanceGateTests (Blocked written, then Skip).


## 33b

AC-HCM-33b — ACCEPTANCE_REQUIRE_ALL=true turns a skipped required scenario into a failure.

Observed: (1) P1, without the stack, filter `AcHcm2|Ac33`: 18 failed (required AcHcm2x scenarios), 1 skipped (Ac33, not required). (2) P6 live run on the clean stack: AC-HCM-26h (required, precondition missing) failed with «Required acceptance scenario blocked (ACCEPTANCE_REQUIRE_ALL=true): No second central hr-core persona…» and recorded Blocked in acceptance-evidence/AC-HCM-26/result.json.


## 33c

Personas: central-a (HR, creator), br1-mgr (step 1, hr-core, RIF), a-management-approver (step 2), an anonymous citizen.
1. central-a: requisition on a free AC33-POOL position (RIF, G5), criteria v1, submit → PendingApproval + RECRUITMENT-REQUISITION-APPROVAL.
   Refusal: a-management-approver gets no approve form at step 1 («هذه الخطوة تتطلب دور «hr-core».») and a direct POST
   of the approval is refused by the server (G-04, PLAN-25) — the instance stays at step 1.
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
   run is echoed by the gateway on the hire, and the gateway audit holds it for the HR.Core hire and the HR.Contracts requests
   (Ac15 pattern). HR.Contracts does not persist or log the message correlation id (follow-up).

## Executed

- 20:14:14 1 requisition
  - after submit: PendingApproval|d0a1a278-499e-4ba1-a04c-2a8061acefa2
- 20:14:17 2 approval and publication
- 20:14:18 3 public application
- 20:14:30 4 selection
  - test-only SQL: interview slot moved one hour into the past (as AC-HCM-26)
- 20:14:39 5 offer
- 20:14:44 6 candidate acceptance
- 20:14:45 7 hire
- 20:14:47 8 contracts and correlation
- 20:14:49 done


## 33d

AC-HCM-33d — evidence folders for AC-HCM-21, 22, 26–33 and AC-WF-23 hold the eight items.

Checked by scripts/hcm033-validate.ps1 (metadata, steps, before, after, audit, db-checks, timing, result + http/*.json) after the P6 run: PASS. AC-HCM-25 does not exist (PLAN-25 deferred).
