Personas: central-a (creator), br1-mgr (step 1 «مراجعة HR», role hr-core, RIF scope), a-management-approver (step 2).
1. central-a creates a draft requisition on a free AC33-POOL position (RIF) and submits it.
2. PendingApproval with a RECRUITMENT-REQUISITION-APPROVAL instance at step 1.
3. br1-mgr approves step 1 → step 2; a-management-approver approves step 2 → Approved (instance Completed, ApprovedDate set).
4. central-a adds criteria v1 and publishes with a closing date 30 days ahead → Published, posting created, closing date stored.