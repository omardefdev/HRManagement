1. central-a creates and submits a requisition on an AC33-POOL position (PendingApproval, step 1).
2. br1-mgr rejects with the reason «PLAN-33 21c: المبررات غير كافية لإشغال الشاغر».
3. The requisition is back to Draft, WorkflowInstanceId cleared; the instance is Rejected and its history holds the reason.
4. Negative: approving the draft afterwards is refused (no workflow) — it stays Draft.
5. Clean-up: central-a cancels the draft so the pool position is free again.