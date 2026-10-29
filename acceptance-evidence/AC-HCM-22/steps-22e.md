Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
1. central-a publishes a requisition on an AC33-POOL position; HR application ranked 1 (test-only SQL precondition).
2. central-a creates and submits an offer → PendingApproval, WorkflowInstanceId set (RECRUITMENT-OFFER-APPROVAL, step 1),
   not Issued; the offer page shows no «لم يُنشأ مسار اعتماد» warning.
3. Clean-up: br1-mgr rejects (offer back to Draft).
The non-lab case (IsLabScale=false + policy Off → PendingApproval without workflow, never Issued) is proven by
src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs (G-02, PLAN-25).