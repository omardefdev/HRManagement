# PAY-001 — Closing Payroll Approval Bypass Paths

## Purpose

Prevent any endpoint from creating a `PaymentBatch` without a properly reviewed and posted `PayrollRun`, enforce the run/batch/line state machine, and reject client-supplied `approverId` values.

## Scope

- Block direct `DbContext` mutations for payroll-sensitive entities
- Unified MediatR Commands/Handlers with state-machine enforcement
- `PaymentBatch` creation only after review + approval + post
- No `SalaryLine` edits after lock; corrections via reversal/adjustment only
- `approverId` derived from authenticated identity, never from client payload
- Maker/checker separation: creator ≠ approver ≠ poster

## State Machine

### PayrollRun Lifecycle

```
Draft ──► Calculating ──► Calculated ──► UnderReview ──► Approved ──► Posted ──► Reversed
  │                           │                                         │
  └──► Cancelled              └── (reject) ──► Calculated              └──► (PaymentBatch created)
```

| Transition | Guard | Actor |
|-----------|-------|-------|
| Draft → Calculating | `ValidateCanCalculate()` | PayrollProcess policy |
| Calculating → Calculated | Job completes all partitions | System |
| Calculated → Validated/Frozen | `ValidatePayrollRunCommand` succeeds | PayrollProcess policy |
| Calculated → UnderReview | `ValidateCanSubmitForReview()`: lines > 0, validated | PayrollProcess policy |
| UnderReview → Approved | `ValidateCanApprove()`, creator ≠ approver | PayrollApprove policy |
| Approved → Posted | `ValidateCanPost()`, approver ≠ poster, audit closed | PayrollPost policy |
| Posted → Reversed | `ValidateCanReverse()` | PayrollPost policy |
| Any pre-post → Cancelled | Not posted or reversed | PayrollProcess policy |

### PaymentBatch Lifecycle

```
(created on Post) Pending ──► Processed
                     │
                     └──► Reversed (via run reversal)
```

- **PaymentBatch is ONLY created server-side** inside `PostPayrollRunHandler` after all guards pass
- No controller endpoint exposes `PaymentBatch` creation
- `TotalAmount` is computed from `SalaryLines`, never client-supplied
- `ProcessPaymentBatchCommand` requires batch `Pending` + run `Posted`

### SalaryLine Immutability

- Lines modifiable only when run is `Draft`, `Calculated`, or `Calculating` AND `ResultsFrozen == false`
- After `ValidatePayrollRunCommand` freezes results, no line edits allowed
- Post-lock corrections use `CreateCorrectionRunCommand` (new run linked to source)

## Bypass Prevention Controls

### 1. No Direct DbContext Mutation

All payroll write operations route through MediatR command handlers. The `AddSalaryLine` MVC action checks `ValidateCanModifyLines()` before any persistence. `PaymentBatch` has no create/edit endpoint — only read (index view).

### 2. ApproverId from Server Identity

```csharp
// PayrollRunsController.ApproveRun
var userId = RequireUserId(); // from JWT claim, not form body
var (success, error) = await _mediator.Send(
    new ApprovePayrollRunCommand(id, userId, "PayrollApprover", null));
```

`ApprovePayrollRunCommand.ApproverId` is always derived from `ClaimTypes.NameIdentifier` / `sub`. Any client-supplied approver value in the request body is ignored.

### 3. Maker/Checker Enforcement

| Check | Location |
|-------|----------|
| Creator ≠ Approver | `ApprovePayrollRunHandler`: `request.ApproverId == run.CreatedBy` → reject |
| Approver ≠ Poster | `PostPayrollRunHandler`: `latestApproval.ApproverId == request.PosterId` → reject |

### 3a. Salary and reference data change by a request (PLAN-53)

| Check | Location |
|-------|----------|
| A base salary or pay grade changes only through `PayProfileChangeRequest` and one applier | `IPayProfileChangeApplier`; `PayProfileWritersTests` names every other writer of `EmployeePayProfile` and fails on a new one |
| Requester ≠ Approver | `DecidePayProfileChangeHandler`: `request.RequestedBy == actor` → 403, state unchanged |
| The decider comes from the server identity | `PayProfileChangeHttp.RequireUserId(User)`; no client field names him |
| A settled month is not rewritten | `PayProfileChangeRules.FindBlockingRunAsync` → 409 naming the month |
| A request is applied only over the profile it was written against | `PayProfileChangeRules.IsWrittenAgainst` → 409 |
| Salary scales and pay grades change by `PayrollReferenceChangeRequest`; a grade in use or a scale with grades is not deleted | `PayrollReferenceChangeApplier`; `PayrollReferenceWritersTests`; the two relations are `Restrict` (PAY053) |
| Every payroll write names its policy; the `payroll` role alone writes nothing | `PayrollWriteActionsTests`; the payroll ceiling list is deleted |

The contract: [PAYROLL_SALARY_CHANGE.md](PAYROLL_SALARY_CHANGE.md). Validation: `scripts/iam053-validate.ps1` (and `-SelfTest`).

### 4. Audit Gate Before Post

`PostPayrollRunHandler` requires a closed `PayrollAuditRecord` before allowing the transition to `Posted`.

## Artifacts

| File | Purpose |
|------|---------|
| `docs/PAY_001_APPROVAL_BYPASS.md` | This document |
| `scripts/pay001-validate.ps1` | Validates all PAY-001 artifacts and patterns |
| `Pay001ApprovalBypassTests.cs` | Architectural scan: no PaymentBatch creation outside PostHandler, approverId not client-bound |

## Acceptance Criteria Mapping

- [x] No endpoint creates `PaymentBatch` before review and posting — batch created only in `PostPayrollRunHandler`
- [x] No `SalaryLine` edits after lock — `ValidateCanModifyLines()` + `ResultsFrozen`
- [x] Amount, actor, and status server-derived, no overposting — `[Bind]` allowlist + server computation
- [x] Same user cannot create, approve, and post — dual-check in `ApprovePayrollRunHandler` + `PostPayrollRunHandler`

## Validation

- `scripts/pay001-validate.ps1`
- `Pay001ApprovalBypassTests`
