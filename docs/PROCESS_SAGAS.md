# Process Sagas (INT-003)

Orchestrated process managers for **hire**, **separation**, **transfer**, and **secondment**.

## Technology

- MassTransit Automatonymous state machines
- EF Core saga repository (PostgreSQL) on Contracts / Movements DbContexts
- Delayed message scheduler for party timeouts
- Inbox (INT-002) on party commands/ACKs

## Ownership

| Saga | Service | Correlation |
|------|---------|-------------|
| `SeparationStateMachine` | HR.Contracts | `SeparationRecordId` |
| `HireCaseStateMachine` | HR.Contracts | `ServiceTransactionId` |
| `TransferStateMachine` | HR.Movements | `TransferRequestId` |
| `SecondmentStateMachine` | HR.Movements | `SecondmentRequestId` |

## Flow

1. Operator completes local prerequisites (DOM-001 checklist with actor/evidence).
2. Publish/Execute starts saga (`IStart*Saga`) — does **not** mean platform-complete.
3. Saga publishes `IExecute*Party` per mandatory party.
4. Party executes (Inbox-safe) and publishes `I*PartyCompleted` / `I*PartyFaulted`.
5. When all parties ACK → saga publishes final domain event (`ISeparationCompleted` / `IHireCaseCompleted` / `ITransferExecuted` / `ISecondmentExecuted`).
6. Timeout or fault → `AwaitingIntervention` + `ProcessInterventions` row (never silent success).

## Mandatory parties

| Journey | Parties |
|---------|---------|
| Separation | Payroll, HR.Core, IDCard, Auth.Directory, Admin.Logistics, IT.Infra, Records.Archive |
| Hire | HR.Core, Payroll, **Auth.Directory**, IDCard, Archive, IT.Infra |
| Transfer | Payroll, HR.Core, Auth.Directory, Admin.Logistics |
| Secondment | Payroll, Auth.Directory |

Configurable via `Sagas:Separation|Hire|Transfer|Secondment:MandatoryParties`.

## Compensation

| Journey | On cancel / compensate |
|---------|------------------------|
| Separation | Re-dispatch `IExecuteSeparationParty` with `Compensate=true` to completed parties (reverse order)؛ **FUN-002:** soft-cancel يحافظ على السجل؛ Payroll يلغي مسودة EOS فقط — انظر FUN_002_RECONCILIATION_CHECKLIST |
| Hire | Intervention + resume؛ **FUN-001:** `ICancelHireSaga` يعيد إرسال parties بـ`Compensate=true` (تعويض خفيف — انظر FUN_001_RECONCILIATION_CHECKLIST) |
| Transfer / Secondment | **FUN-003:** `ICancelTransferSaga` / `ICancelSecondmentSaga` → Compensating؛ soft-cancel على السجلات؛ Loan عبر `ILoanAssignmentEnded` — انظر FUN_003_RECONCILIATION_CHECKLIST |

## Human intervention

- Table: `ProcessInterventions`
- API: `GET/POST /api/process-interventions` (list, resume, cancel) on Contracts and Movements
- Resume republishes `IResume*Saga` without manual SQL

## DOM-001 gate

PLAN-60 replaced the publication with the execution (`ExecuteSeparationCommand`), and it is the only path that starts the separation saga
(`HrContractsSeparationPostPoliciesTests` finds any other call of `StartSeparationSagaAsync`). It runs only on an `Approved` record with its
numbered decision (`DEC-SEP-YYYY-#####`), its pre-execution items done or excepted (the decision, the employee's notification), the
effective date reached in Damascus's time, no legal hold and a valid service regime. In one save with the outbox it ends the contract at the
effective date (`ContractWorkflowRules.Terminate(contract, endDate)`), completes `ContractTerminated` as a System item, moves the record to
`Executing` with its actor and its transaction along, and stages `IStartSeparationSaga`. No person completes `ContractTerminated` by hand.

The record's status follows the saga: a party's fault or timeout (`IProcessInterventionRequested`) → `AwaitingIntervention`; Resume →
`Executing`; `ISeparationSagaFinalized(Success)` → `Completed` (the transaction by `SeparationRecordId`, every party's item acknowledged).
Each acknowledgement completes its PartyAck item (`SeparationPartyAckChecklistConsumer`); a compensation's completes nothing. Cancelling an
executing separation (the interventions' cancel included) is refused until PLAN-63's compensation path.

## Metrics (`HRManagement.Saga`)

`saga_started`, `saga_completed`, `saga_timed_out`, `saga_intervention_open`, `saga_compensated`
