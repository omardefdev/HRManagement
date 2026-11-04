1. SQL: an asset in storage, a PendingApproval assignment and its ASSET-ASSIGN-APPROVAL instance.
2. Gateway API: central-a approves step 1, a-management-approver step 2 -> within 60 s the assignment is Active and the asset InUse.
3. Negative: a second assignment rejected at step 1 -> Rejected, the asset is not InUse.