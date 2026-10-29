Seed: REQ-2026-A-AC33-PEND2 (PendingApproval, step 2 «اعتماد إداري», role hr-management-approver).
1. br1-mgr (hr-core + branch-manager, in scope, not an approver) opens Details: the server-computed panel shows
   «هذه الخطوة تتطلب دور «hr-management-approver».» and no approve form.
2. Negative: br1-mgr POSTs ApproveWorkflow and RejectWorkflow directly → both refused by WorkflowStageGuard; status,
   step and history unchanged (before PLAN-25 the role was checked in the UI only — G-04).