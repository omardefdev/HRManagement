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

## P3 — Four eyes, and the channel's confirmation
- PayrollDualControlRequest (new table; Kind/Status as int; unique pending (tenant, kind, target) IX_PayrollDualControlRequests_OnePending;
  xmin; ExpiresAt stored at the request, Payroll:DualControl:ExpiryHours default 72) and PayrollDualControlService (RequestAsync,
  DecideAsync with the act inside the decision's transaction, InboxAsync, ExpireDueAsync; lazy expiry on read and on decision).
- Kinds and policies: HoldRelease and CriticalAlertResolve asked by Payroll.Approve; ArrearsAdjust asked by Payroll.Process; reissue,
  settlement and rejection asked by Payroll.Post; every decision Payroll.Approve by a person who is not the requester (400 otherwise,
  403 without the kind's policy, 409 decided meanwhile, 400 expired). A rejection needs a note and changes nothing.
- Decision taken: requests are made on their target's route under the kind's policy (payment-holds/{id}/release,
  fraud-alerts/{id}/resolution-requests, arrears/{id}/adjustment-requests, payment-batches/{id}/settlement-requests,
  /rejection-requests, /files/{fileId}/reissue-requests); api/payroll/dual-control is the inbox (GET), one request (GET {id}) and the
  decision (POST {id}/decide). A generic POST of any kind would need a policy admitting every requester; not added.
- Removed from the client: ReleasePaymentHoldCommand (with ApproverUserId), the hold form's approver field, the alert form's approver
  field, the arrears' approverId (Arrears.Update is now a request under Payroll.Process). A non-critical alert is resolved by one act
  and releases no hold; a critical one is a request whose decision resolves it and releases its hold.
- REGIME-BLOCKER release: HR.Core's client must answer «not blocked» at the decision (no client: refused).
- §7: ack is one act of Payroll.Post with the channel's reference (required); settlement and rejection are requests; the settled amount
  must equal the acknowledged orders' sum (400 at request and at decision, nothing changed); Settle/Reject handlers are reached from
  DecideAsync only (path test rule).
- Screens: DualControl/Inbox «طلبات بانتظار قرارك» (+ menu link), batch page confirmation forms and reissue request per file, hold,
  alert and arrears pages without an approver field.
- Migration regenerated (two tables, RLS on both).
- Tests: Pay058DualControlTests (8, SQLite: every kind, requester refused, expiry with a clock, regime blocker, settlement mismatch and
  settle by two, rejection, reissue), Pay058DisbursementHttpTests + approver id in the body ignored (PostgreSQL, P7). Frd001 and
  Pay055 PG tests moved to the service. Integration filter 210 passed; architecture 440; unit filter 1183.
Finished (UTC): 2026-11-26T13:22:58Z

## P4 — The double approval, the independent review, the gateway
- Double approval (from P1): PayrollRun.Approve counts distinct approvers of the present version against
  sod.GetRequiredApprovalCount("PayrollRun", TotalNetPay) with Payroll:Approval:DualApprovalThreshold; the API answers status,
  approvalsGiven, approvalsRequired, progress («اعتماد 1 من 2»); the run page shows it with the review's state and the held employees by
  name. A lost race on the run's row version reaches the conflict middleware (409), as PLAN-56's test expects.
- Policies (ServiceDefaults): Payroll.AuditReview (PayrollReviewerRequirement: roles from Payroll:Audit:ReviewerRoles, auditor by default)
  and Payroll.RunRead (the module's users or the reviewer).
- Review: Open/ClosePayrollAuditRecordCommand (opener and closer differ; neither is the run's creator, any approver, or its poster; the
  gate's warning never closes). PayrollAuditRecordsController [ModulePolicyOverride(Payroll.AuditReview)], writes name AuditReview,
  RunSummary page for the reviewer; PayrollAuditRecordsApiController (POST runs/{id}/audit-records, POST audit-records/{id}/close,
  GET audit-records/antiforgery); GET runs/{id} moved to PayrollRunReadApiController (override Payroll.RunRead). Two org-scope allowlist
  rows removed (PaymentBatches in P2, PayrollAuditRecords here: the list shrank).
- Gateway: GatewayPayrollReviewPaths (review screens, review API, run read, static files); every other payroll path keeps Module.Payroll.
- Posting: poster is none of the approvers (every approval) and none of the reviewers; Payroll:Audit:Required=false posts without a review
  and writes a line in the run's log.
- Tests: GatewayAuditorReachTests (4, architecture project: reach, limits, setting, real actions), Pay058ApprovalReviewTests (6, SQLite).
  Integration filter 184 passed; unit filter 1760; architecture 440 + 4.
Finished (UTC): 2026-11-26T13:36:18Z

## P5 — Conflicting roles
- PayrollRoleConflictRequirement + handler (Payroll.Web/Authorization), added by AddPayrollRoleConflictCheck to Payroll.Process,
  Payroll.Approve, Payroll.Post, Payroll.Reverse and Payroll.AuditReview (in Payroll only). Pairs from SeparationOfDutiesService.
  hr-admin counts as processor and approver everywhere, and poster where Payroll.Post keeps him (demo, lab). Refusal: 403 with the
  pair named (IAuthorizationMiddlewareResultHandler: JSON on the API, a short Arabic page on a screen). SecurityAudit RoleConflict
  (new event type) with policy, pair, endpoint route pattern and rejected; written only for the act's own authorization (the request
  as the resource), not for a page that asks whether to show a button. Payroll:Sod:RejectConflictingRoleHolders=false lets the holder
  pass, still audited.
- Settings (P-15) in Payroll's appsettings.json and mirrored in docker-compose.acceptance-prodlike.yml: Payroll:Approval, :Audit,
  :Blockers, :Sod, :DualControl.
- Import guard: scripts/keycloak/persona-role-conflicts.ps1 (dot-sourced by acceptance-personas.ps1; -SelfTest with planted personas);
  AcceptancePersonaCatalog also exempts SodConflictFixture only.
- Personas (P-9, P-8): acc-a-payroll-multirole (payroll, processor, approver, poster, sodConflictFixture) and acc-a-auditor-2 (auditor),
  agency A HQ ordinals 49 and 50; AC-PAY-58 added to the scenarios of the processor, approver, approver-2, poster and auditor.
  AGENCY-A regular personas 47 -> 49 (70 in all); acceptance-verify DATA-AUTH 68 -> 70, DATA-DIRECTORY 59 -> 61. A persona change needs
  scripts/acceptance-reset.ps1 before the first stack start (P8).
- hr-admin tests moved to «reaches the policy, refused by the conflict»: PayrollWriteActionsTests (Pay055 reach, the sixteen, legal
  attest) evaluate Payroll's conflict check now (conflictCheck: false keeps the policies' role rules).
- Tests: Pay058RoleConflictTests (8: each pair, the multirole holder, hr-admin by environment, setting off, import guard self-test and
  pairs). Unit 1277 passed (personas, realm, SoD, payroll); architecture 444.
- **For P7 (PostgreSQL HTTP tests that give one user conflicting roles, now refused):** Pay055BankAccountHttpTests (CentralRoles
  processor+approver), Iam053PayrollScopeHttpTests, Pay054FrameworkHttpTests (BothUser), and any hr-admin payroll act in
  Pay056RunLifecycleHttpTests / Iam053ReferenceChangeHttpTests / Lva057PayrollAttendanceReadsHttpTests: split the roles, meaning kept.
Finished (UTC): 2026-11-26T13:47:51Z

## P6 — Holds per employee, real fraud inputs
- Gate per employee: a hold (of the run, or with no run, in every batch), a critical alert of that employee, a NO-ACCOUNT or a
  retired account holds that employee's order only (Held with its reason); the agency is stopped only by a critical alert with no
  employee. process with every order held: 400, nothing changed.
- Service regime (D-PAY-4): PayrollRegimeGovernance.DecideAsync runs before BeginCalculating. Payroll:Blockers:Mode=HoldEmployee
  (default) calculates everyone and adds a REGIME-BLOCKER hold of the run per blocked employee with HR.Core's reason code (the order is
  Held REGIME-BLOCKER, released only by four eyes once HR.Core lifts it); BlockRun refuses before the run moves (Draft, no job, no hold).
  The check left PayrollRunParallelCalculator.
- Account verification (§6 rule 3): VerifyBankAccountHandler runs in one transaction; HeldOrderRepinning re-pins the employee's unsent
  Draft/Held orders and their lines to the account now in force, lifts NO-ACCOUNT/ACCOUNT-RETIRED holds (registrar and verifier are the
  two people), writes EntityAuditLog RepinnedOnAccountVerification with ids only; a sent batch then sends it by submit-held.
- Fraud inputs real (FraudScanFacts): FR-01 from PLAN-57 receipts (absent days >= working days of the period), FR-05 from the last
  posted regular run's net, FR-07 from the earliest pay-profile EffectiveFrom, FR-08 true for a correction (its source run), FR-04
  from pinned accounts. Released-hold suppression limited to fraud holds. **Deviation:** docs/frd/anomaly-rules.json FR-04 action
  flag-for-review -> payment-hold, so AC-PAY-58g holds the two employees' orders (Frd001FraudEngineTests updated, meaning: both held).
- P-11: xmin on DeductionRecord and PayrollAdjustment; a calculation that loses the race fails its partition with
  PayrollInputTakenException (no double consumption); the package save paths throw it too. Proof on PostgreSQL in P7.
- Migration regenerated once: 20261126135338_PAY058_DisbursementGovernance (no pending model changes).
- Tests: Pay058HeldEmployeeTests (7: no-run hold across two batches, REGIME-BLOCKER named, BlockRun untouched, NO-ACCOUNT lifted by
  verification then submit-held with a HeldRelease file, FR-04 both held, FR-01 from a receipt and a correction without FR-01/FR-08,
  inputs raising FR-01/05/07). Syr007RegimeGovernanceTests ported to DecideAsync (both modes). Unit filter 1048, architecture 444,
  integration (SQLite) 211 passed.
Finished (UTC): 2026-11-26T14:11:51Z

## P7 — The PostgreSQL proofs
- Pay058DisbursementPostgreSqlTests (5, app role under forced row security, tenant wiring): two concurrent process calls of one
  batch (a save barrier makes both read Pending) -> one sends, the other 409, one Initial file; two concurrent decisions of one
  hold-release request -> one decides (the request's xmin), the hold released once; a failure planted in the posting's save that
  writes the orders -> no batch, no order, run still Approved, and the same posting then makes one batch; one employee blocked by
  FakeHrCoreServiceRegimeClient -> calculated, his order Held REGIME-BLOCKER with HR.Core's reason code, the others sent, his
  release refused (RegimeStillBlocked) until HR.Core lifts it, then sent alone by a HeldRelease file; P-11: two runs of two months
  calculating at once over one free deduction -> one takes it, the other stops with PayrollInputTakenException.TakenMessage and
  writes no line.
- Pay058MigrationPostgreSqlTests (2): PAY058 on LVA057's schema owned by a NOSUPERUSER NOBYPASSRLS role with FORCE on every tenant
  table, two agencies: old approvals at Revision 1, old batches Source 0 and ArrearsAmount 0, old orders without hold reason or file,
  amounts' checksum unchanged; PaymentBatchFiles and PayrollDualControlRequests under forced row security with tenant_isolation
  (USING and WITH CHECK), cross-agency write refused, one pending request per target and one file per sequence (named indexes);
  down to LVA057 and up again.
- PostgreSQL HTTP classes that gave one token two conflicting payroll roles (refused since P5) now give the same person two tokens,
  meaning kept: Pay055BankAccountHttpTests, Iam053PayrollScopeHttpTests (the generated scope sweep runs once per role, a controller
  counts as exercised if either pass reached it), Iam053ReferenceChangeHttpTests (the own-request rule with an approver's token of
  the requester; hr-admin's decision now 403), Pay056RunLifecycleHttpTests (foreign-agency acts by the role each policy asks for;
  the poster's own approver token still gets DeciderIsPoster, the conflicting token 403), Pay054FrameworkHttpTests (BothUser edits
  as processor and is refused as approver by the editor rules). Iam052PgServiceHost gained RestrictedAs/CentralAs(roles)/WithRoles.
- Pay055TestAccounts.Iban: derived from a hash of the whole id. **Finding:** the old derivation took the GUID's first 22 hex digits,
  so the sequential ids b0000001-...-000000000001/2/3 shared one IBAN; FR-04 (a hold since P6) held all three and the ported
  Pay055BankAccountPostgreSqlTests sent nothing. Test data fixed, meaning kept.
- Full Testcontainers step (CI's filter Category=Testcontainers&Category!=ObjectStorage, Debug): 332 passed, 2 failed (the two
  PLAN-54 HTTP classes above, fixed and rerun green), 1 skipped; 18 min.
Finished (UTC): 2026-11-26T15:00:33Z

## P8 — Acceptance, then the independent review
- Pieces: AcPay58DisbursementGovernanceTests (58a..h, prodLike main, required); AcceptancePayrollReview (review by API: acc-auditor
  opens, acc-a-auditor-2 closes; the second approval by acc-a-payroll-approver-2 when approvalsRequired is 2) and
  AcceptancePayrollAccounts (no acceptance seed gives an employee a bank account, so a class that sends first gives a few of its own
  employees a verified account by PLAN-55's two people; partial funding pays in employee-id order, so 58a and AC-24 pick the lowest
  ids). AC-24 rewritten (funding decision on the posted batch, poster sends, file = sent orders, funded + arrears = total; 400 before
  the posting, 404 partial-disburse, 403 processor, 409 second sending). AC-25 rewritten (FR-04: two employees paid into one account
  held, a third sent; four-eyes release; HeldRelease file; no shared account left), month August 2027 (June is AC-12's). AC-PAY-56
  and AC-12 meet the second approval and the review; AC-PAY-55a and AC-PAY-54b: hr-admin's acts now 403 with the conflict named (the
  editor/self-verification rules stay proved by the HTTP integration tests with one person, two tokens). AC-UX-67 probes the inbox,
  a batch's page and, as the auditor, the review records' list, form and run summary. AcceptanceV2Catalog: the one marked fixture is
  exempt from the SoD persona check. scripts/acceptance-payroll-audit-fixture.ps1 deleted. ci-scope (AcPay58 required, prodLike main),
  ac-ownership (AC-PAY-58 owned by PLAN-58, AC-24/25/55 reasons). scripts/pay058-validate.ps1 (+ -SelfTest, 22 planted defects).
- A read-only reference check of the new tests before the first stack (my PLAN-57 lesson) found 5 test defects (file counts that
  ignored other classes' accounts, the funding order, 54b's attest answer, absolute leftovers, 'Critical' vs 'critical') and one
  product issue: the funding decision read the orders with no ORDER BY, so who was funded first was not deterministic. Fixed
  (OrderBy EmployeeId, Id) with a test.
- Images: all 16 service images built one at a time in 25 min (payroll-web rebuilt twice after fixes, ~2 min each).
- Prod-like stack (fresh, 16 min to ready): AcPay58 8/8 on the first run (58a: total 276,124,665, 60% funded, approvals 2 of 2);
  then AC-PAY-54/55/56, AC-IAM-50..53, AC-LVA-57, AC-HCM-25: 55/55; AC-UX-67 7/7 (the new pages 200, one policy, no violation);
  isolated group 46/47 — AC-37b needs the prod-like journey (an E2E class, CI-only per P-14), recorded, not chased.
- Independent review (fresh agent; no high finding). Fixed with tests (Pay058ReviewFixTests, architecture rule):
  M1 a bank file whose orders were acknowledged/settled/rejected is not reissued (request and writer); M2 only the reviews of the
  run's present version count (opened after its last rejection); M3 a reviewer of the present version does not approve, and a review
  closed by an approver does not count at the posting; L1 the run's approve, post and fraud-scan API validate the antiforgery token
  (approve's signature changed; clients updated: AC-12, Pay056RunLifecycleHttpTests); L2 one alert per run, employee and rule
  whatever its status (a two-person resolution is not undone by the next scan); L3 voided arrears are owed to nobody; L4 the ack runs
  in its own transaction; L6 the bank-file writer is required; L8 PaymentOrderService.TryAdvanceStatusAsync (no caller, a latent
  second way to submit) removed, and an architecture rule: an order becomes Submitted only in the coordinator. M4 the rollback note
  (Held is written at the first posting; Voided arrears) is in OPS_VPS_SINGLE_HOST.md. Not changed, recorded: L5 Payroll.RunCancel
  is not one of the five financial policies (P-9 names five); L7 the gate is read before the sending's transaction (a hold created
  in that window is caught by the next sending); L9 old partial batches (none on the demo: 0 orders); L10 BlockRun is checked at the
  calculation's start, a resumed job continues the checked snapshot; L11 by design.
- After the fixes: unit filter 1052, architecture 444, payroll integration (SQLite) 209 + 5 new, full Testcontainers step 334 passed
  / 1 skipped (one HTTP class needed the poster's token, fixed).
- Lab stack (fresh): AC-21, 23, 24, 25, AcceptanceV2Catalog (9), AC-HCM-29 (3) pass; AC-12 Blocked («the run could not be submitted
  for review»), the PLAN-59 block recorded on main, not chased. A first lab run found my own collision (AC-25 had taken June 2027,
  AC-12's month: moved to August), AC-24 writing an empty answer as a default JsonElement, and the catalog's SoD check not exempting
  the one marked fixture; fixed and rerun on a fresh stack.
- Prod-like rerun after the review fixes (fresh stack): AC-PAY-54, 55, 56, 58 — 25/25. Seeded counts by acceptance-verify: 64/64
  (DATA-AUTH 70, DATA-DIRECTORY 61, DATA-PAYPROFILE 1,489, SEED-03).
- Least privilege (migrator image rebuilt with PAY058, 21 min): AC-OPS-38 8/8 (first run: 38a «admin-logistics-web is not ready»,
  passed on the rerun); AC-TEN-48's one test answered 502 twice: it ran first, at the stack's start, 13 s before HR.Core logged the
  end of its start-up (the gateway: «an error was encountered before receiving a response»). The readiness race recorded in PLAN-56's
  lessons; nothing of this plan touches HR.Core or the national settings. Left to the PR run (required there).
- Not run locally (P-14): the E2E journeys (CI only), so AC-37b (needs the prod-like journey) is not counted.
- P9: origin/main has not moved (9b069f65): nothing to merge.
Finished (UTC): 2026-11-26T18:33:10Z

## P10 — The demo server (waiting for the owner)
- SSH works (`hrm`, host key unchanged). /opt/hrm/current-tag = 0dd3564560b6 (previous d6b6880ead87), both commits of the owner's
  open PR #58 (`ops/ghcr-release-images`), on no branch of origin but that one: `git branch -r --contains 0dd3564560b6` →
  origin/ops/ghcr-release-images only. Server disk 61 G free.
- P-17's «when» fails: the running tag is not on origin/main. Deploying PLAN-58 (built from main) would replace the owner's GHCR
  release (its deploy path and the hr-rabbitmq package fix are not on main), and main documents no GHCR path yet. Nothing was
  changed on the server.
- BLOCKED: 2026-11-26T18:34:41Z — the owner decides: merge PR #58 first (then P9 merges main here and P10 follows the
  documented GHCR path), or another way for the server step.
- Owner's answer «ادمجه واكمل» (2026-11-26T18:37Z): PR #58 had all 15 checks green and was CLEAN; merged by me with
  `gh pr merge 58 --merge` at 2026-11-26T18:38:30Z (merge commit 34001753d409). P9 then merged origin/main into the branch
  (f4afd0aa; one conflict in OPS_VPS_SINGLE_HOST.md, two appended sections, both kept; Ops036 unit tests 85/85 after it).
- main now documents the GHCR path and says nothing is built on the server any more (the loopback registry is gone), so P10
  builds the images on GitHub Actions from the pushed release branch: the push comes before the server checks (deviation recorded).
- AcPay58VpsChecks written (5 checks: migration and picture, removed paths 404, hradmin's conflict refusal audited, an arrears
  request rejected by the approver with nothing changed, the screens under the CSP); the report written before the local gates.
- Local gates on the merged branch: ci-local (security scan's NuGet timeout; its steps rerun, summary 87 passed, 5 not run), the
  Release build (found CS8629 as an error: `bc4703ce`), gitleaks clean. `release/plan-58` (`25be64cf`) pushed, PR #59.
- GHCR build: `deploy.yml action=build` on `release/plan-58` (run 45889506130, 20:13:52–20:24:48Z): 17/17 images `25be64cf9fa8`.
- Backup `/opt/hrm/backups/pre-plan58-20261126T201439Z` (verified); the read-only picture as at P0.
- Deploy `scripts/vps/deploy.ps1 -Tag 25be64cf9fa8` (previous 0dd3564560b6), 20:25:30–20:31:19Z, first time; smoke 50/0/1 warn.
- AC_PAY_58_vps_* 5/5 (20:33–20:44Z). Left: one rejected arrears adjustment request.
- Older plans' checks (20:44:51–23:33:46Z): 72/78. The six: four PLAN-54/55/56 checks written for hradmin acting in payroll (now
  refused, D-PAY-20; adapted in 64dfffff and 4a6f21ae), AC_POL_47 (the local cause PLAN-53 recorded), AC_WF_46 (a 5,000-line tail
  of a log that grew 27,267 lines in 40 min).
- PR #59 CI meanwhile: run 1 the prod-like stage at its 35-min limit (limits raised by QA-066's rule: 55 and 60); run 2 the
  architecture rule «no return after a result» (restructured); run 3 (45901561330, 4a6f21ae) 15/15 green, 62 min.
- Reruns on the demo (23:35–23:59Z): PAY-55, PAY-56 reversal and screens pass; PAY-54's cycle hit its own query (`||` with no
  attestation is NULL, the row vanished): coalesced, then passed; AC_WF_46 passed alone after the 23:58:01 sweep.
- Final smoke 23:59:41Z (`-RequireIntegrationWiring`): 50 pass, 0 fail, 1 warn. Demo up on 25be64cf9fa8, 54 G free.
- Evidence the local runs rewrote (acceptance-evidence, testdata/*/out, a replay report) restored or moved under artifacts/.
Finished (UTC): 2026-11-27T00:04:45Z

## P11 — Docs, the final CI run, the merge
- Docs updated: OPS_VPS_SINGLE_HOST.md (PLAN-58's section, the change table rows 1–8, what the checks leave), the final report
  (§12, §13, §16). The PAY-54 server check's query fix and the documents pushed; one more full CI run on the head before the merge.
- CI run 45914768450 (b74faeef): 14/15; prodlike failed on AC-IAM-52a «central-a's list does not show the ALEP record». Cause: the
  test's random free day was 2025-03-05, in a month AC-LVA-57 fills for every employee of its tenant (same stage), and one day's list
  shows its first 100 records ordered by date only. Fixed in the test's day choice (fewer than half a page on that day); the
  assertion is unchanged. Not this plan's code; possible since PLAN-57 put AC-LVA-57 in the prod-like main group.
