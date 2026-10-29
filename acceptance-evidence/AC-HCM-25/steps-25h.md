Seeded on purpose (test-only SQL, as a pre-PLAN-25 build left them): a pool requisition PendingApproval without a
workflow, and an InProgress RECRUITMENT-HIRE instance (orphan) on an application.
1. scripts/hcm025-repair-stuck-approvals.ps1 (dry run, acc-a-directory-checker = hr-admin, HrCoreDelete) lists both; nothing changes.
2. With -Apply: the requisition gets RECRUITMENT-REQUISITION-APPROVAL (still PendingApproval), the orphan is Cancelled,
   each with an audit row; a second dry run no longer lists them. Clean-up: the requisition is rejected and cancelled.