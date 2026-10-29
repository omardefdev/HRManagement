Profile: prod-like (NationalScope:IsLabScale=false, PolicyEngine:Core.Recruitment=Shadow, Recruitment:Approvals:AlwaysStartWorkflow=true);
the active profile is recorded in before.json / db-checks.json.
1. central-a creates a requisition on a free AC33-POOL position and submits it → PendingApproval with a
   RECRUITMENT-REQUISITION-APPROVAL instance (before PLAN-25: no instance — G-02).
2. br1-mgr approves step 1, a-management-approver step 2 → Approved, instance Completed.