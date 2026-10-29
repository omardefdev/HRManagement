Seed: REQ-2026-A-AC33-PEND1 (PendingApproval, step 1 of RECRUITMENT-REQUISITION-APPROVAL, created by central-a).
1. central-a opens Details: the creator block reason «لا يمكن للمنشئ اعتماد طلبه الخاص.» is shown and there is no approve form.
2. br1-mgr (control) opens the same Details and does get the approve form.
3. Negative: central-a POSTs ApproveWorkflow directly → refused; status, step and history unchanged.