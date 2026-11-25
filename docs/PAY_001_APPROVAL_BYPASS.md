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

### PayrollRun Lifecycle (as built, PLAN-56)

```
Draft ──► Calculating ──► Validating ──► Calculated ──► UnderReview ──► Approved ──► Posted ──► Reversed
  ▲            │                                            │                          │   (an approved reversal request)
  │            └── restart a failed/paused calculation ──► Draft                       └──► (PaymentBatch created at the posting)
  └──────────────────── reject with a note (Revision + 1) ──┘

Draft, Calculating, Validating, Calculated, UnderReview, Approved ──► Cancelled   (with a reason; D-PAY-18)
```

The state is written in one place (`PayrollRun.Move`, through `PayrollRunTransitions`). `PayrollRun.Status` has no setter;
product code cannot assign a state around a transition (`Pay056TransitionTests`, `scripts/pay056-validate.ps1`).

| Transition | Guard | Actor |
|-----------|-------|-------|
| Draft → Calculating | `BeginCalculating` (`ValidateCanCalculate()`) | PayrollProcess policy |
| Calculating → Validating → Calculated | Job completes all partitions; `ValidateLineIntegrity` passes; results frozen (`MarkValidated`) | System |
| Calculated → Calculated (validate again) | `ValidatePayrollRunCommand`: only a run being calculated or calculated; a run sent for review, approved, posted, reversed or cancelled is never brought back | PayrollProcess policy |
| Calculated → UnderReview | `ValidateCanSubmitForReview()`: lines > 0, validated | PayrollProcess policy |
| UnderReview → Approved | `ValidateCanApprove()`, creator ≠ approver | PayrollApprove policy |
| UnderReview → Draft | `ReopenAfterRejection`: the note is mandatory; the rejected version is kept as a `PayrollRunRevision`, its lines deleted, its inputs released | PayrollApprove policy |
| Approved → Posted | `ValidateCanPost()`, approver ≠ poster, audit closed; the saved lines still add up to the frozen total (the posting writes no line); `PostedBy`/`PostedAt` recorded | PayrollPost policy |
| Posted → Reversed | An approved `PayrollReversalRequest`: requested with a reason (`Payroll.Reverse`), decided by another person who is neither the requester nor the poster (`Payroll.Approve`); refused once a batch or an order has left (D-PAY-16) | PayrollReverse + PayrollApprove policies |
| Any pre-post → Cancelled | `Cancel`: reason mandatory; jobs stopped, inputs released; D-PAY-18: the processor before the run was sent for review, the approver after | PayrollRunCancel policy |
| Calculating (failed or paused job) → Draft | `RestartCalculationCommand`: results not frozen; job cancelled, lines deleted, inputs released, then a new calculation with a new snapshot | PayrollProcess policy |

### PaymentBatch Lifecycle

```
(created on Post) Pending ──► Submitted ──► Acked ──► Settled
                     │             └──► Rejected / Returned
                     └──► Voided   (an approved reversal of a run whose batch has not left: Pending, Failed or Rejected)
```

- **PaymentBatch is ONLY created server-side** inside `PostPayrollRunHandler` after all guards pass
- No controller endpoint exposes `PaymentBatch` creation
- `TotalAmount` is the run's frozen total, never client-supplied
- `ProcessPaymentBatchCommand` requires batch `Pending` + run `Posted`
- A batch moves only through `PaymentBatchTransitions`. A reversal writes no batch: `PaymentBatchKind.Reversal` stays in the
  enum for rows written before PLAN-56, and nothing creates one any more

### SalaryLine Immutability

- Lines modifiable only when run is `Draft`, `Calculated`, or `Calculating` AND `ResultsFrozen == false`
- After `ValidatePayrollRunCommand` freezes results, no line edits allowed
- Post-lock corrections use `CreateCorrectionRunCommand` (a new run linked to its posted source, made of the approved adjustments aimed at it: PLAN-56)
- A rejection or a restart deletes the run's lines (the rejected version's fingerprint is kept in `PayrollRunRevision`); no person edits or adds a line

## Bypass Prevention Controls

### 1. No Direct DbContext Mutation

All payroll write operations route through MediatR command handlers. The manual salary line page is removed (PLAN-56): a line is written by the calculation only, with its components. `PaymentBatch` has no create/edit endpoint — only read (index view).

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
