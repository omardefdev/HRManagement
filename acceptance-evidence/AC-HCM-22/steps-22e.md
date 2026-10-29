Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
1. central-a publishes a requisition on an AC33-POOL position; HR application ranked 1 (test-only SQL precondition).
2. central-a creates and submits an offer → PendingApproval, WorkflowInstanceId set (RECRUITMENT-OFFER-APPROVAL, step 1),
   not Issued; the offer page shows no «لم يُنشأ مسار اعتماد» warning.
3. Clean-up: br1-mgr rejects (offer back to Draft).
G-02 is closed (PLAN-25, decision D-1): the submit starts the workflow whatever IsLabScale or the policy mode say.
The non-lab case is proven by Hcm033LabScaleBehaviourTests (integration) and, with the Production values on
PostgreSQL, by Hcm025ProductionApprovalIntegrationTests; live on the prod-like profile by AC-HCM-25b.