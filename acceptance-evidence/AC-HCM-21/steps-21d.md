Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
1. central-a submits a new requisition → WorkflowInstanceId set, definition RECRUITMENT-REQUISITION-APPROVAL, InProgress at step 1.
2. The Details page shows the workflow panel and no «لم يُنشأ مسار اعتماد» warning.
3. Clean-up: br1-mgr rejects, central-a cancels (frees the pool position).
G-02 is closed (PLAN-25, decision D-1): the submit starts the workflow whatever IsLabScale or the policy mode say.
The non-lab case is proven by Hcm033LabScaleBehaviourTests (integration) and, with the Production values on
PostgreSQL, by Hcm025ProductionApprovalIntegrationTests; live on the prod-like profile by AC-HCM-25a.