# DFD Datastore Audit (D1–D12)

**Purpose:** Map logical DFD datastores (D1–D12) to implementing services and physical tables, with gap notes from the 2026-06 gap-closure pass.

| Store | DFD label | Primary service | Tables / entities | Gap notes |
|-------|-----------|-----------------|-------------------|-----------|
| D1 | Employee master | HR.Core | `Employees`, `EmploymentRecords`, `Qualifications` | CV approval wired to `ApprovalWorkflowService`; movement consumers append `ApprovedCV` notes |
| D2 | Org structure | HR.Core | `OrgUnits`, `OrgUnitSnapshots` | Materialized path + integration publish |
| D3 | Movements | HR.Movements | `TransferRequests`, `SecondmentRequests`, `LoanAssignmentRequests`, `DistributionPlans`, `MovementExecutionRecords` | `TransferRequestId` on plans; `ILoanAssignmentExecuted`; workforce gap via `WorkforceGapAnalysisRules` |
| D4 | Contracts & separation | HR.Contracts | `Contracts`, `SeparationRecords`, `SeparationChecklists` | `ISeparationStepCompleted` on checklist completion |
| D5 | Training | HR.Training | `TrainingPlans`, `TrainingNominations`, `LearningEnrollments` | Nomination→plan link; ESS publishes `ITrainingCompleted` at 100% |
| D6 | Payroll | Payroll | `PayrollRuns`, `PayrollAuditRecords`, `SalaryLines` | Post requires closed audit; variance report between runs |
| D7 | Compliance | Compliance | `Complaints`, `LegalReferrals`, `ComplianceRecommendations` | `ComplaintId` FK; Refer/AddRecommendation actions |
| D8 | Archive | Records.Archive | `ArchiveItems`, `DisposalRecords`, `LoanRecords` | `CanDispose()` enforced before disposal |
| D9 | Directory / IAM | Auth.Directory | `DirectoryEntries`, `AuthUsers` | Transfer updates org unit; IT.Infra sync via `IDirectoryEntrySyncService` |
| D10 | Analytics | Dev.Analytics | `AnalyticsEvents`, `ReadModels`, `ETLJobs` | Complaint/CardIssued consumers; ETL extract to ReadModel |
| D11 | Logistics assets | Admin.Logistics | `Assets`, `AssetAssignments` | Transfer consumer returns assignments |
| D12 | IT ops | IT.Infra | `BackupJobs`, `ITSupportTickets` | Backup `Run` pg_dump stub |

## Cross-cutting

- **Message mesh:** See [MESSAGE_EVENT_MATRIX.md](./MESSAGE_EVENT_MATRIX.md).
- **Idempotency:** `IntegrationMessageReceipts` on Payroll medical consumer and peripheral consumers.
- **SchemaVersion:** Documented on `IComplaintResolved` as versioning example.

## Residual gaps (non-blocking)

- Workforce gap UI relies on manual headcount or Dev.Analytics API until live headcount feed lands in D1.
- Auth.Directory HTTP sync from IT.Infra requires `Services:AuthDirectory:BaseUrl` in deployment config.
- Loan assignment views not yet in module nav (controller registered via convention routing).
