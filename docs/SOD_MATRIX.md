# SoD Matrix — فصل الواجبات (IAM-003)

## Maker / Checker / Poster / Reverser

| EntityType | Maker | Checker | Poster | Reverser | Different actors | Financial threshold | Min approvals |
|---|---|---|---|---|---|---|---|
| PayrollRun | payroll-processor | payroll-approver | payroll-poster | payroll-poster / payroll-reverser | yes | 50,000,000 SYP | 1 (2 above threshold) |
| LeaveRequest | hr-contracts | hr-contracts | — | — | yes | — | 1 |
| AdvanceRequest | hr-contracts | hr-contracts | — | — | yes | 5,000,000 SYP | 1 (2 above threshold) |
| TransferRequest | hr-movements | hr-management-approver | — | — | yes | — | 1 |
| ApprovalRequest | hr-core | hr-management-approver | — | — | yes | — | 1 |
| RoleAssignment | auth-directory | hr-admin | — | — | yes | — | 2 (four-eyes) |
| LegalDataChange | hr-core | hr-management-approver | — | — | yes | — | 2 (four-eyes) |
| EosSettlement | payroll-approver | payroll-poster | — | — | yes | 50,000,000 SYP | 1 |
| SelectionCommitteeSession | hr-core (creator / secretary) | hr-core or branch-manager (steps 1–2), hr-management-approver (step 3) | — | — | yes — the creator approves no step; committee members do not approve step 3; quorum re-checked from step 2 | — | 3 steps |
| RecruitmentAppeal | hr-core (files on behalf, assigns reviewer, resubmits) | compliance — the assigned independent reviewer only | — | — | yes — the reviewer is not the applicant, a committee member of the requisition, an interview panel member, an assessor, the filer or the assigner | — | 1 |

## Conflicting role pairs

| Role A | Role B | Reason |
|---|---|---|
| payroll-processor | payroll-approver | لا يجوز الجمع بين إنشاء التشغيل واعتماده |
| payroll-approver | payroll-poster | لا يجوز الجمع بين الاعتماد والترحيل |
| payroll-processor | payroll-poster | لا يجوز الجمع بين الإنشاء والترحيل |
| hr-core | hr-management-approver | الكاتب لا يعتمد قرارات الإدارة العليا لنفس الكيان |

## Recruitment committee and appeal (PLAN-28)

- Checked on the server (`SelectionCommitteeRules.ValidateApproval`, `RecruitmentAppealService`), not only in the UI.
- Committee members are employees (employee picker); the actor's `employee_id` claim identifies them. An approver without
  the claim is subject to the creator rule only.
- The appeal reviewer is identified by employee (`IndependentReviewerEmployeeId` = `employee_id` claim) and must hold
  `compliance`; independence is checked when the reviewer is assigned. Legacy appeals keep their Keycloak user id.

## Trusted identity

- Approval actors are taken **only** from the authenticated token (`NameIdentifier` / `sub`).
- Client-supplied `approverId` form/query parameters were removed from Transfer, Leave, Advance, and EmploymentRecords approve/reject actions.

## Delegation

PLAN-50 (D-WF-5, D-50-1..D-50-5). One entity, `ApprovalDelegation` (`WorkflowDelegation` was removed).

| Rule | Where |
|------|-------|
| One role per delegation (`RoleScope`, mandatory), from `Delegation:DelegableRoles` (`hr-management-approver` only), held **directly** in the principal's own token at creation | `SeparationOfDutiesService.ValidateDelegation(d, DelegationRules)`; the check constraint `CK_ApprovalDelegations_ActiveHasRole` |
| No chain: a delegated role never reaches a token, a claim or `IsInRole`, so a delegate cannot delegate it again | `RoleOrDelegationRequirement`, the evaluator, the movement acting context are the only decision points |
| Optional `EntityTypeScope`, from the closed list `Delegation:EntityTypes` | `ValidateDelegation`; `ApprovalDelegationMatcher` |
| At most 90 days, ending within 90 days of the creation; no self-delegation, by user or by employee | `ValidateDelegation` (`DelegationRules.CreatedAt`) |
| The delegate is an employee chosen from HR.Core's directory (`DelegateEmployeeId`), matched with the `employee_id` claim; a row older than PLAN-50 that holds only `DelegateId` is matched with `sub` | `ApprovalDelegationMatcher.IsDelegate` |
| A delegate approves nothing that he or his principal created: every identity rule runs twice (as the delegate, then as the principal) | `WorkflowApprovalEvaluator`, the inbox's bulk check, `MovementApprovalActing.CreatorOrPrincipal`; on behalf, no movement of the delegate's or the principal's own employee: `MovementApprovalActing.Subject` |
| A delegate never gets a wider org scope than his principal had: a branch-scoped principal's scope is stored on the delegation (`PrincipalScopeOrgUnitId`) and applies to the delegate | `WorkflowActionActor.PrincipalOf`, `MovementApprovalActing.PrincipalScopeAllowsAsync`; a reader of transfers and loans admitted by a delegation lists and opens only that scope (`MovementApprovalActing.AdmittedBy`) |
| The direct role wins: a holder of the role acts in his own name | evaluator, acting context |
| A revocation acts at once in HR.Core (no cache) and within `Delegation:CacheSeconds` (60) in HR.Movements, which asks HR.Core's service-only endpoint; any lookup failure means no delegation | `DbApprovalDelegationLookup`, `HttpApprovalDelegationLookup` |
| Every creation and revocation writes an `EntityAuditLog` row; the step history, the signature and the movement approval record name the principal («بالنيابة عن») | `DelegationsController`, `WorkflowInstanceActionService`, `TransferApproval`, `MovementChecklist` |

Managed on `/ui/hr-core/Delegations` (policy `SoD.ManageDelegations`; the gateway table admits the approver on `Create` and `Revoke`).
A user sees the delegations he made and those made to him; `hr-admin` and `platform-admin` see the tenant's. The migration
`HCM050_ApprovalDelegationScope` revoked every row in force with no role or a role that is not delegable ("PLAN-50 migration").
Out of scope (wave 2): other delegable roles, `HireWizard.Step3Approve` and the legacy `Approvals`, automatic absence, escalation,
revoking a leaver's delegations.

## Reporting

Run periodic conflict detection:

```powershell
pwsh ./scripts/sod-report.ps1 -ConnectionString $env:HRCORE_CONNECTION -OutputPath ./artifacts/sod-report.json
```

## Privileged roles (four-eyes grant)

- `hr-admin`
- `platform-admin`
- `payroll-poster`

## Legal fields requiring dual approval

- NationalId, DateOfBirth / BirthDate, HireDate / EmploymentDate, FullName (First/Last)

## Workflow engine rules (PLAN-43)

These hold for every workflow type, on the server and in the inbox panel (`WorkflowSodRules`). `Workflow:AdminBypass` does not pass them.

| Rule | Who is barred | From |
|------|---------------|------|
| Excluded | The human requester of a service start, and `ExcludedApproverUserIds` | Approving and rejecting every step |
| Subject | The employee the request is about (`SubjectEmployeeId`) | Approving and rejecting every step |
| DistinctApprover | Anyone who approved another level of the same instance, when the definition sets `RequireDistinctApprovers` | Approving |

No service account approves or rejects a step, or writes a definition. A definition is published by a person who neither created, edited nor submitted it.

The recruitment and establishment rules stay in their gates. Exempt types: `docs/qa/workflow-step-role-exemptions.json`.

## Custody and loss (PLAN-45, D-WF-2)

`ASSET-ASSIGN-APPROVAL` and `ASSET-LOSS-APPROVAL`, with `RequireDistinctApprovers`, and a guardrail that keeps them so:

| Step | Role | Barred |
|------|------|--------|
| 1, موافقة مسؤول الأصول | `admin-logistics` | The requester (`CreatedBy`, also excluded by name) and the employee concerned |
| 2, اعتماد إداري | `hr-management-approver` | The same, and whoever approved step 1 |

`ExternalApprovalGate` keeps a branch-level actor inside the unit the instance recorded; an instance with no unit is for central roles. `admin-logistics` reaches the work inbox and the approval page, and no other page of HR.Core: it is in `HrCoreWorkInbox` and `WorkflowStepAction`, never in `ModuleHrCore` or `HrCoreReadOnly`.

The requester, or any `admin-logistics` in the asset's scope, withdraws a request with a reason of at least 10 characters. The actor and the reason are recorded, and nothing is deleted.

## ID card and discipline (PLAN-46, D-WF-1, D-ER-2)

Both are approved in HR.Core's engine (`WF_001_WORKFLOW_ENGINE.md`, «الاعتماد العابر للخدمات»). The owning service starts the
instance with the people to exclude; the engine bars them from every step, and the owner refuses an outcome that names them anyway.

| Definition | Step | Role | Barred |
|------------|------|------|--------|
| `ID-CARD-ISSUANCE` | 1, مراجعة مكتب البطاقات | `idcard` | The requester (`RequestedBy`, `CreatedBy`), the submitter, and the employee concerned |
| `DISCIPLINARY-APPROVAL` | 1, مراجعة امتثال | `compliance` | The creator, the submitter, and the employee concerned |
| `DISCIPLINARY-APPROVAL` | 2, اعتماد صاحب الصلاحية | `hr-management-approver` | The same, and whoever approved step 1 (`RequireDistinctApprovers`) |

- A card is issued only from an `Approved` request; whoever holds `idcard` may process it, because the two pairs of eyes were the
  requester and the reviewer. `Renew` opens a replacement request.
- A disciplinary action reaches payroll only when approved; the actions published before PLAN-46 are `ApprovedBeforeEngine` and are
  never published again.
- `idcard` and `compliance` reach the inbox and the approval page of HR.Core, nothing else: they are in `HrCoreWorkInbox` and
  `WorkflowStepAction`, never in `ModuleHrCore` or `HrCoreReadOnly`.
- No entity type is exempt from the step role since PLAN-46.

## The maker (PLAN-44, D-WF-8)

`WorkflowMakerRules.Makers` is the creator (`CreatedBy`), the submitter (`InitiatedBy`, with `Workflow:Maker:IncludeSubmitter=true`) and the requester (`RequestedByUserId`), leaving out the empty ones. They are barred from every step, approving and rejecting, in the requisition, offer, establishment, competition and result gates. The inbox asks the same function, so the panel and the server bar the same people. Results bar the submitter whatever the setting says.

The committee and the appeal keep their own rules: the session's creator, and the assigned reviewer.

The legacy path of `ApprovedCV` checks the step's role, and only the requester or `hr-admin` cancels.
