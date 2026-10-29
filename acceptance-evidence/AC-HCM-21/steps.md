## 21a

Personas: central-a (creator), br1-mgr (step 1 «مراجعة HR», role hr-core, RIF scope), a-management-approver (step 2).
1. central-a creates a draft requisition on a free AC33-POOL position (RIF) and submits it.
2. PendingApproval with a RECRUITMENT-REQUISITION-APPROVAL instance at step 1.
3. br1-mgr approves step 1 → step 2; a-management-approver approves step 2 → Approved (instance Completed, ApprovedDate set).
4. central-a adds criteria v1 and publishes with a closing date 30 days ahead → Published, posting created, closing date stored.

## 21b

Seed: REQ-2026-A-AC33-PEND1 (PendingApproval, step 1 of RECRUITMENT-REQUISITION-APPROVAL, created by central-a).
1. central-a opens Details: the creator block reason «لا يمكن للمنشئ اعتماد طلبه الخاص.» is shown and there is no approve form.
2. br1-mgr (control) opens the same Details and does get the approve form.
3. Negative: central-a POSTs ApproveWorkflow directly → refused; status, step and history unchanged.

## 21c

1. central-a creates and submits a requisition on an AC33-POOL position (PendingApproval, step 1).
2. br1-mgr rejects with the reason «PLAN-33 21c: المبررات غير كافية لإشغال الشاغر».
3. The requisition is back to Draft, WorkflowInstanceId cleared; the instance is Rejected and its history holds the reason.
4. Negative: approving the draft afterwards is refused (no workflow) — it stays Draft.
5. Clean-up: central-a cancels the draft so the pool position is free again.

## 21d

Live stack = acceptance profile (NationalScope:IsLabScale=true, policy engine Off).
1. central-a submits a new requisition → WorkflowInstanceId set, definition RECRUITMENT-REQUISITION-APPROVAL, InProgress at step 1.
2. The Details page shows the workflow panel and no «لم يُنشأ مسار اعتماد» warning.
3. Clean-up: br1-mgr rejects, central-a cancels (frees the pool position).
G-02 is closed (PLAN-25, decision D-1): the submit starts the workflow whatever IsLabScale or the policy mode say.
The non-lab case is proven by Hcm033LabScaleBehaviourTests (integration) and, with the Production values on
PostgreSQL, by Hcm025ProductionApprovalIntegrationTests; live on the prod-like profile by AC-HCM-25a.