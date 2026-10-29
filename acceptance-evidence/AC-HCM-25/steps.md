## 25a

Profile: prod-like (NationalScope:IsLabScale=false, PolicyEngine:Core.Recruitment=Shadow, Recruitment:Approvals:AlwaysStartWorkflow=true);
the active profile is recorded in before.json / db-checks.json.
1. central-a creates a requisition on a free AC33-POOL position and submits it → PendingApproval with a
   RECRUITMENT-REQUISITION-APPROVAL instance (before PLAN-25: no instance — G-02).
2. br1-mgr approves step 1, a-management-approver step 2 → Approved, instance Completed.

## 25b

Profile: prod-like (recorded in db-checks.json). Precondition (test-only SQL, ranking is AC-HCM-26): an HR application on a published pool requisition is Ranked 1.
1. central-a creates the offer at the G5 mid salary and submits it → PendingApproval with RECRUITMENT-OFFER-APPROVAL at step 1.
2. br1-mgr approves step 1, a-management-approver step 2 → Issued.

## 25c

Seed: REQ-2026-A-AC33-PEND2 (PendingApproval, step 2 «اعتماد إداري», role hr-management-approver).
1. br1-mgr (hr-core + branch-manager, in scope, not an approver) opens Details: the server-computed panel shows
   «هذه الخطوة تتطلب دور «hr-management-approver».» and no approve form.
2. Negative: br1-mgr POSTs ApproveWorkflow and RejectWorkflow directly → both refused by WorkflowStageGuard; status,
   step and history unchanged (before PLAN-25 the role was checked in the UI only — G-04).

## 25d

1. central-a: published pool requisition, an Assessed HR application (test-only SQL), a Ranking session with the
   seeded roster (chair + 3, session a0000001-0000-4000-8000-000000000924), unanimous votes, a decision, submit.
2. br1-mgr approves step 1 → quorum step (2).
3. Negative: two members are recused (test-only SQL) → 2 active; br1-mgr POSTs step 2 → refused
   «النصاب غير مكتمل…» (CommitteeApprovalGate), still step 2; the panel shows the same reason.
4. Positive: the recusals are reverted → br1-mgr approves step 2 → step 3; a-management-approver approves → ApprovedAt.

## 25e

1. central-a enters an application on the seeded published requisition, rejects it, files an appeal and assigns a-compliance.
2. Negative (gate): a-compliance-2 POSTs a Decide for it → refused (AppealApprovalGate: not the assigned reviewer).
3. Negative (role): central-a (hr-core, allowed into the appeal pages) sees the server-computed reason
   «هذه الخطوة تتطلب دور «compliance».» on the appeal and its Decide POST is refused — the review step requires «compliance»
   (the actor role is no longer hard-coded to Compliance).
4. Positive: a-compliance decides «Upheld» → Decided, instance Completed.

## 25f

Seed: public posting a0000001-0000-4000-8000-000000000908.
1. a-management-approver (Module.HrCore lets the role into HR.Core) opens the posting Details (read is allowed).
2. Negative: POST PublicJobPostings/Edit → 403 (explicit [Authorize(Policy = HrCoreWrite)] since PLAN-25, G-15;
   the gateway's HrCoreWrite rule for mutating requests refuses it as well). The posting is unchanged.

## 25g

Live facts on the running stack (patched on start-up by WorkflowPackSeeder.RetireAsync and
PolicyEngineDomainSeeds.DeactivateRuleIfPresentAsync):
1. R-REC-HIRE has no effect («[]») and its name is marked «[معطّلة]»; R-REC-ORG and R-REC-HIRE-OFFER remain.
2. RECRUITMENT-HIRE is unpublished with ValidTo set.
3. No RecruitmentApplication workflow instance is in progress (hires on this stack — AC-HCM-22a/30/33c — started none).
The Enforce-mode hire itself (fresh DB, pre-PLAN-25 DB with rule + published definition + binding, static fallback)
is proven by Hcm025RecruitmentHireRetiredTests (unit) — Enforce is not the deployed mode (Production = Shadow).

## 25h

Seeded on purpose (test-only SQL, as a pre-PLAN-25 build left them): a pool requisition PendingApproval without a
workflow, and an InProgress RECRUITMENT-HIRE instance (orphan) on an application.
1. scripts/hcm025-repair-stuck-approvals.ps1 (dry run, acc-a-directory-checker = hr-admin, HrCoreDelete) lists both; nothing changes.
2. With -Apply: the requisition gets RECRUITMENT-REQUISITION-APPROVAL (still PendingApproval), the orphan is Cancelled,
   each with an audit row; a second dry run no longer lists them. Clean-up: the requisition is rejected and cancelled.