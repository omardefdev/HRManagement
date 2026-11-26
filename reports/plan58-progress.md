Started (UTC): 2026-11-26T11:50:02Z

Main folder: /home/omar/HRManagement (owner's Linux notebook). Keep-awake: systemd-inhibit PID in artifacts/plan58/keepawake.pid.

## P0 — Timing, keep-awake, preconditions, baseline

- Preconditions: no other plan session (the owner's GHCR work runs in its own worktree /home/omar/HRManagement-ghcr, PR #58, not a plan);
  origin/main = 9b069f65 (PLAN-57's merge); tree clean; ssh hrm-vps -> hrm, ED25519 key SHA256:qWIfQls8...Gsto matches; gh omardefdev
  with repo+workflow. All pass.
- Branch feature/plan-58-pay-disbursement-governance from origin/main (upstream unset). PLAN-57's merge note cherry-picked (db7ec3c0),
  the branch docs/plan-57-merge-note deleted after a content check. Prompt written from the session's text and committed (774f264e).
- Baseline (reports/plan58-baseline.md): build 4 min 41 s green; unit filters 483 passed; pol049 FAIL (28) as on main.
- Inventories: grep (artifacts/plan58/inventory.*) + one read-only subagent (~8 min) for P-3's lists.
- Demo picture taken read-only. **Finding:** the demo runs 0dd3564560b6 from the owner's open PR #58 (not on origin/main); P-17's
  condition is checked again at P10.
- Decisions noted for later phases (recorded with reasons in the final report):
  - FR-04 (shared pinned account) is high/flag-for-review in docs/frd/anomaly-rules.json, so it holds nothing; AC-PAY-58g requires the
    two orders held -> FR-04 becomes payment-hold (P6).
  - The run route POST runs/{id}/funding-decisions stays as an alias that answers 400 on a run that is not posted (AC-PAY-58a's
    negative) and acts on the posting's batch otherwise; the batch route is the primary one.
  - FR-07 now reads the earliest profile date, so AC-25's «hired and paid the same day» fixture no longer raises it: AC-25 needs another
    real fraud trigger (decided in P8).

Finished (UTC): 2026-11-26T12:11:44Z

## Next step
P1: PaymentBatchIssuer, posting makes batch+orders, funding on the batch, process/submit-held, partial disbursement removed, seeder,
ArrearsSettlementService, reversal lift, Pay058SinglePaymentPathArchitectureTests.

## P1 — One payment path (in progress, code done, gates running)

Decisions (with reasons):
- PaymentBatchIssuer (Payroll.Application/Services) makes the batch and one order per line with a positive net; a line with no
  pinned account gets a Held order NO-ACCOUNT. Lines with net <= 0 (a correction's recovery) pay nothing and get no order. The batch's
  TotalAmount stays the run's TotalNetPay; the sum rule compares orders (not cancelled) + batch.ArrearsAmount with the payable net
  (sum of positive nets = TotalNetPay for every regular run).
- New columns: PaymentBatch.Source (int: Legacy 0, PayrollRunPosted 1, Seeded 2), PaymentBatch.ArrearsAmount (the funding's arrears,
  a snapshot, so a later arrears adjustment does not break the sum rule), PaymentOrder.HoldReason, PaymentOrder.PaymentBatchFileId,
  PayrollRunApproval.Revision (approvals counted on the present version). New enum values: PaymentOrderStatus.Held=7 (by name),
  ArrearsLedgerStatus.Voided=3 (by name) -> rollback note (P-17).
- The per-employee gate (P6's) landed with P1's process: the sending loop needs it (NO-ACCOUNT, held orders).
- process with nothing sendable (every order held) refuses 400 and changes nothing (no empty sending, no empty file); the held employees
  are released and process is called again. A second process of a sent batch is 409.
- Coordinator methods change tracked rows and save nothing; the handler saves once in a transaction, then checks the sum rule on what
  was saved, then commits (rollback on a broken rule).
- The funding decision: on a Pending batch of a Posted run, once (409 for a second); RequestedAmount = the run's payable net (the
  client's requestedAmount is gone); an order allocated 0 is Cancelled with a note (its whole net is arrears). The run route
  POST runs/{id}/funding-decisions stays as an alias (400 for a run that is not posted; AC-PAY-58a's negative).
- The settle handler records one bank statement line per settlement (reference + settled amount): the old per-order import collided on
  the unique statement reference for a batch of more than one order (an older defect this plan's manual settlement would have met).
- Reversal: the refusal stays for legacy funding/partial batches; a funding on the posting's batch that never left is reversed
  (batch Voided, orders Cancelled, arrears rows Voided with the reversal's reason).
- Approve: PayrollRun.Approve(approver, requiredApprovals) counts distinct approvers of the present version; the run row is written by
  every approval (row version) so two concurrent approvals cannot both count without one losing (409).
- ArrearsSettlementService (tests only) returns an ArrearsSettlementBatch descriptor, not a PaymentBatch.
- PLAN-56's tests: messages kept where the meaning is the same (PosterIsApprover keeps the old text).
Files: domain (PaymentBatch, PaymentOrder + PaymentOrderHoldReasons, PaymentOrderStatus Held, ArrearsLedgerStatus Voided,
PaymentBatchSource, PaymentBatchFileKind, PayrollRun.Approve/ApprovalsGiven, PayrollRunApproval.Revision, ArrearsLedgerEntry.VoidForReversal),
application (PaymentBatchIssuer + PaymentBatchLedger, PaymentBatchHandlers.cs: process/submit-held/ack/settle/reject, funding decision on
the batch, options PayrollGovernanceOptions.cs, IPaymentBatchFileWriter seam, gate/coordinator abstractions, posting/approval handlers,
reversal lift), infrastructure (gate per employee, coordinator, order service, reconciliation, seeder), web (PaymentBatchesApiController,
PayrollApiController without partial-disburse, MVC messages), tests (Pay058SinglePaymentPathArchitectureTests with 8 planted cases,
Pay058PaymentPathTests 6, PartialDisbursementOrchestratorTests ported, PayrollWriteActionsTests Pay058 rows + reach, others adapted).
Tests: unit full 7244 passed (8 min 7 s); integration payroll filter (no containers) 175 passed + Pay058 6; architecture 414 + 108 passed.
Finished (UTC): 2026-11-26T12:48:35Z

## P2 — The bank file
- PaymentBatchFile (new table, Kind as int, unique (batch, sequence) IX_PaymentBatchFiles_Batch_Sequence, xmin), PaymentBatchFileBuilder
  (today's CSV columns + PaymentOrderId, Amount; closing line CONTROL,count,total,batch,sequence,kind; UTF-8 without BOM; SHA-256),
  PaymentBatchFileWriter (next sequence; orders keep the file id; ReissueAsync: new sequence, the old file SupersededById, refuses a
  superseded file). process/submit-held write the file in their transaction (the handler's answer carries the file id).
- Download: GET api/payroll/payment-batches/{id}/files/{fileId} and the batch page's button; Payroll.Post or Payroll.Approve (403
  otherwise); SecurityAudit BulkExport with resource payment-batch-file:<id> and controls in the detail (no account).
- Removed: ExportTreasuryFile and ExportExternalIntegrationBundle (the bundle's other half is ExportSocialInsuranceFile, kept);
  PayrollExternalExportService is insurance-only and no longer takes the disbursement gateway; the treasury refusals
  (Missing/RetiredPinnedBankAccountsException) went with the file; PLAN-55's naming rule kept as PayrollEmployeeNames.Listed.
  The shared adapters (IPayrollDisbursementGateway, FilePayrollDisbursementGateway, the INT-004 live connector) stay in Shared: Payroll
  no longer calls them (the path test refuses them in src/Services/Payroll).
- Batch screens: PaymentBatchesController (own file, [OrgScopeCentralOnly]; its org-scope allowlist row removed - the list shrank),
  Views/PaymentBatches/Details.cshtml (orders by state, held card by name, files with download, funding form, sending buttons).
- Migration PAY058_DisbursementGovernance regenerated (adds PaymentBatchFiles + RLS) with artifacts/plan58/tools/regen-migration.sh
  (snapshot from 9b069f65, Revision default 1, EnableRls block, no BOM on the migration files).
- Tests: Pay058BankFileBuilderTests (3, unit), Pay058BankFileTests (2, SQLite), Pay058DisbursementHttpTests (download, PostgreSQL: runs in
  P7), Pay055BankAccountPostgreSqlTests ported to the bank files (PostgreSQL: P7). Unit filter 628 passed, arch 421 passed, integration
  (SQLite payroll filter) 28 passed.
Finished (UTC): 2026-11-26T13:07:51Z
