Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
1. central-a submits a new requisition → WorkflowInstanceId set, definition RECRUITMENT-REQUISITION-APPROVAL, InProgress at step 1.
2. The Details page shows the workflow panel and no «لم يُنشأ مسار اعتماد» warning.
3. Clean-up: br1-mgr rejects, central-a cancels (frees the pool position).
The non-lab negative (IsLabScale=false + policy Off → PendingApproval without a workflow and the warning) cannot be
produced on the shared lab stack; it is proven by the integration test
src/Tests/HRManagement.IntegrationTests/Hcm033LabScaleBehaviourTests.cs (G-02, PLAN-25).