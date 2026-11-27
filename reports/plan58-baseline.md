# PLAN-58 baseline (P0, «المرحلة 0»)

Taken 2026-11-26 on `feature/plan-58-pay-disbursement-governance` (from `origin/main` = `9b069f65`, PLAN-57's merge), on the owner's Linux
notebook. Generated inventories are under `artifacts/plan58/inventory.*` (not committed); this file summarises them.

## 0.2 Build and tests

| Step | Result |
|------|--------|
| `dotnet build HRManagement.slnx -warnaserror` | 0 warnings, 0 errors, 4 min 41 s |
| Unit tests, filter `Pay001\|Pay004\|Frd001\|Ext003\|SeparationOfDuties\|Pay055\|Pay056\|Lva057` | 483 passed, 0 failed (39 s) |
| `scripts/pol049-validate.ps1` (red on `main`, not this plan's) | `FAIL (28)`, as recorded by PLAN-57 |

## 0.3 Money paths (every `new PaymentBatch`, every sending, every payment file)

Product code (tests construct fixtures freely and are not a path):

| Where | What | Becomes |
|-------|------|---------|
| `PostPayrollRunHandler` (`PayrollRunHandlers.cs:256`) | the posting's `Pending` batch, no orders | `PaymentBatchIssuer` (source `PayrollRunPosted`): the batch and one order per line in the posting's save |
| `PartialDisbursementService.Allocate` (`:134`) | a batch per partial disbursement | no batch: it allocates; the funding decision applies the allocation to the posting's orders |
| `ExecutePartialDisbursementHandler` → `EnsureAndSubmitOrdersAsync` (`:144`) | an `Approved` run's batch, orders sent at once, no review, no poster, no fraud scan, no gate | deleted, with `POST runs/{id}/partial-disburse` |
| `ArrearsSettlementService.Settle` (`:60`, `:117`) | an in-memory batch, callers `Pay004ArrearsTests` only | a settlement descriptor that is not a `PaymentBatch` (PAY-I pays arrears later) |
| `DemoPayrollSeeder.SeedPaymentBatchesAsync` (`:481`) | the seed's batches | `PaymentBatch.Seeded(...)`, allowed there only |
| `ProcessPaymentBatchHandler` → `EnsureAndSubmitOrdersAsync` (`PayrollRunJobHandlers.cs:542`) | the one other sending | stays, with the per-employee gate and the bank file; `submit-held` is the second caller |
| `PayrollReportsController.ExportTreasuryFile` (`:255`) and the treasury half of `ExportExternalIntegrationBundle` (`:313`), via `PayrollExternalExportService` → `FilePayrollDisbursementGateway` | a payment file from every line's net, unlimited, ignoring holds, funding and state | removed; the bank file is `PaymentBatchFile`, built from the orders of one sending |
| `PayrollExportJobService` (run CSV, register, EOS, bulk payslips) | reports: no account number, not an instruction to pay | kept (not a payment file) |

Existing partial batches made by the removed path: **none on the demo** (0 funding decisions, picture below). The acceptance seeds make
none (Ac24/Ac25 made theirs at run time).

## 0.4 Actor ids taken from the client (Payroll)

| Where | Field | Becomes |
|-------|-------|---------|
| `PaymentHoldsController.Release` (`:75-90`), `Views/PaymentHolds/Details.cshtml:35` | `releaseApprovedBy` / form `ReleaseApprovedBy` | a four-eyes request (`HoldRelease`) decided by another session |
| `PaymentHoldsController.Release` (`:105`) | sets the alert's `ApprovedBy` from it | the decision's own actor |
| `PaymentHoldsApiController.Release` (`:25-39`), `ReleasePaymentHoldCommand.ApproverUserId` | body `ApproverUserId` | removed from the body (ignored if sent); the call asks for a release |
| `FraudAlertsController.Resolve` (`:101-133`), `Views/FraudAlerts/Details.cshtml:59` | `approvedBy` / form `ApprovedBy` | a non-critical alert: one act; a critical one: a request (`CriticalAlertResolve`) |
| `ArrearsController.Update` (`:95-102`) | `approverId` | a request (`ArrearsAdjust`) |
| `CreateFundingDecisionCommand.ApprovedBy` | from the token already (`RequireUserId()`) in the API | stays from the token; on a batch |

Every other actor in Payroll's commands comes from `RequireUserId()` / the token.

## 0.5 Personas (`testdata/acceptance/personas.json`, 68)

The payroll-relevant ones: `e2e-payroll-processor` (processor), `e2e-payroll-approver`, `acc-a-payroll-approver-2` (approvers),
`acc-a-payroll-poster`, `acc-a-payroll-reverser`, `acc-auditor`, `acc-platform-auditor`, `acc-b-auditor` (`auditor`), agency B's processor
and approver, `acc-c-payroll`, and `acc-a-directory-checker` (`hr-admin`). **No persona carries a conflicting pair in its token.**
`hr-admin` is counted as processor and approver by D-PAY-20 (it passes `Payroll.Process` and `Payroll.Approve` everywhere, and
`Payroll.Post`/`Reverse` on the lab stack and the demo). This plan adds `acc-a-auditor-2` and the fixture `acc-a-payroll-multirole`.

## From the prompt (P-3)

**Classes that approve or post a run** (inventory by a read-only subagent, checked against the files):

| Class | Stack | Run | Approve / post | Meets |
|-------|-------|-----|----------------|-------|
| `Ac12PayrollSodTests` | lab | June 2027 | approver approves; no post | the double approval if the lab run is above the threshold |
| `Ac24PartialDisbursementTests` | lab | April 2027 | approver approves, 60 % funding, processor `partial-disburse`; never posted | rewritten (D-PAY-2): posting, review by two auditors, funding on the batch, `process` by the poster |
| `Ac25FraudHoldTests` | lab | current month | approve, SQL audit fixture, post, funding, `partial-disburse`, `process` blocked, release with `approverUserId`, `process` | rewritten: four-eyes release by request, per-employee hold, review by API |
| `AcPay56RunStateMachineTests` | prod-like | Jan–Jun 2028 | approve, SQL audit fixture (`CloseAuditAsync`), post; 56e processes May's batch (and forces it `Submitted` by psql if the gate refuses) | second approval, review by two auditors |
| `AcPay56VpsChecks` | demo | — | reversal requests only | the conflict refusal of `hradmin` (Payroll.Reverse on the demo) |
| E2E | — | — | none approves or posts | — |

On the prod-like stack agency A's runs (about 1,100 lines) are far above 50,000,000: every approval there meets the double approval.

**Users of `scripts/acceptance-payroll-audit-fixture.ps1`:** `Ac25FraudHoldTests.cs:102`, `AcPay56RunStateMachineTests.cs:283-293`
(`CloseAuditAsync`, called at `:365`, `:800`, `:840`, `:1046`); docs only elsewhere (`docs/CI_AND_TESTING.md:82`, the gap studies, older
prompts and plans). Not in `ci-scope.json`, `.github` or a validator.

**Where `hr-admin` reaches a payroll financial act (D-PAY-20 changes these):**
- `PayrollWriteActionsTests.cs:447-453` (Admin reaches every PLAN-55 write), `:617-625` (the «Sixteen» Payroll.Process writes), `:706`
  (LegalAttest = Payroll.Approve).
- `Iam051ModuleFilterAndPoliciesTests.cs:193-229` (Admin on Post/Reverse by environment: stays, it is the policy's reach).
- `AcPay55BankAccountTests.cs:34` (`a-directory-checker` registers an account: Payroll.Process), `AcPay54LegalFrameworkTests.cs:42-43,
  :420, :446` (hr-admin's approve/attest/publish refused by the handler).
- Demo checks: `AcPay54VpsChecks.cs:38`, `AcPay55VpsChecks.cs:40, :344`, `AcPay56VpsChecks.cs:13, :50, :421-426` (`hradmin` asks for
  reversals), `AcIam51VpsChecks.cs:161-177`.
- Docs: `docs/SOD_MATRIX.md:10, :19-26`, `docs/OPS_VPS_SINGLE_HOST.md`, `docs/PAYROLL_SALARY_CHANGE.md:54`.

**Readers of a set of order, batch or arrears statuses:** `PaymentOrderTransitions` (`CanTransition`, `HasLeft`),
`PaymentBatchTransitions` (`HasNotLeft`, `IsPaid`), `BankAccountPinning.DisbursedOrderStatuses`/`UnpaidBatchStatuses`,
`PayrollReversalPayments.IsLive`/`MoneyHasLeft`, `PaymentOrderService` (target switch), `PaymentOrderCoordinator`, the reconciliation
(`BankReconciliationCoordinator.cs:16`, `BankReconciliationService.cs:42,55`), the batch handlers, `ArrearsSettlementService` (open = not
`FullyPaid`), `ArrearsLedgerEntry` (status from amounts), views `PayrollRuns/Details.cshtml:147`, `Arrears/Index.cshtml:35,78-81`,
`Arrears/Details.cshtml:28`, `PaymentBatches/Index.cshtml:28`; seeders; acceptance SQL in `AcPay56`/`AcPay55` and their demo checks.
`Held` is not «left» and not «disbursed»; `Voided` arrears are not open.

**Holds with no run:** `InboundEventHandlers.RaiseReviewHoldAsync` (`PAY029-NOSAL`, `PAY029-RANGE`, `PAY055-ACTIVATION`, type Manual,
with a Medium alert with no run); `FraudAnomalyEngine.EvaluateProofOfLifeExpiry` (no product caller). Readers that treat «no run» as
«every run»: the gate (`PaymentDisbursementGate.cs:21-25`, today it blocks the whole batch), the fraud scan's dedup. After this plan such a
hold holds that employee's orders in every batch, never the others.

## The demo's picture (P-16), read only, 2026-11-26 ~12:20Z

- **Running tag `0dd3564560b6`** (previous `d6b6880ead87`): both from the owner's open PR #58 (`ops/ghcr-release-images`, the GHCR move),
  **not on `origin/main`** — P-17's «when» is checked again at P10. Disk: 145 G, 84 G used, **61 G free**.
- `payroll_db`: runs by status (int) `1|Regular|1`, `2|Regular|4`, `4|Regular|1`, `8|Regular|10` (1 calculated, 4 posted, 1 approved,
  10 cancelled); batches `Pending|1`, `Processed|5`; **orders 0; holds 0; alerts 0; funding decisions 0**; arrears `FullyPaid|5`,
  `PartiallyPaid|5`, `Pending|5`; audit records `Open|1`; approvals `Cancelled|10`.
- Checksums (md5 of id+status+amount rows; no amount or account printed): runs `16|f3e7fb6493ab266e0bf3cbbad17ab2d3`, lines
  `2550|b2dbd262436b4eeda983509bcc3cc323`, batches `6|31924d4517c7a7d8112ac29885438542`, orders `0|`, arrears
  `15|747ae32f3066ecc513aa302862d79c82`. Script: `artifacts/plan58/vps/picture.sh`.
