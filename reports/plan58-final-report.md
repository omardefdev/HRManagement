# PLAN-58 — Disbursement governance: one payment path, real four eyes, and holds per employee (PAY-F)

Branch `feature/plan-58-pay-disbursement-governance`, released as `release/plan-58`. Started 2026-11-26T11:50:02Z.
Progress, decisions and every stop: `reports/plan58-progress.md`. Baseline: `reports/plan58-baseline.md`.

## 1. Summary per phase

| Phase | What it did |
|---|---|
| P0 | Baseline (build, unit filters, `pol049-validate` red on main as recorded), the inventory of every money path, every actor taken from the client, the personas' roles, the classes that approve or post, and the demo's read-only picture. |
| P1 | One payment path: the posting makes the batch and its orders (`PaymentBatchIssuer`, in the posting's transaction); a funding decision comes after it, on a `Pending` batch; only the poster's `process` and `submit-held` send. `partial-disburse` and every other batch maker removed; an architecture test holds it. |
| P2 | One bank file per sending (`PaymentBatchFile`: Initial, HeldRelease, Reissue), built from the sent orders with a control row (count, total), stored with its SHA-256 and read back as stored; the treasury file and bundle removed. |
| P3 | Every four-eyes act is a request and a decision by two people (`PayrollDualControlRequest`, inbox «طلبات بانتظار قرارك», 72 h expiry); the channel's confirmation: the poster's ack, then a settlement or rejection another person decides, so a batch reaches `Settled`. |
| P4 | Above `Payroll:Approval:DualApprovalThreshold` (50,000,000) two different approvers of the run's present version («اعتماد 1 من 2»); the independent review by `auditor`, opened by one auditor and closed by another, through the gateway, before the posting. |
| P5 | A token that carries two conflicting payroll roles passes none of the five financial policies (403 naming the pair, a `RoleConflict` security audit line); `hr-admin` counts as processor and approver; the realm import refuses a conflicting persona but the one marked fixture. |
| P6 | Holds per employee: a hold, a critical alert, no account, a retired account or a service-regime blocker holds that employee's order only; `Payroll:Blockers:Mode=HoldEmployee` (default) or `BlockRun`; a verified account re-pins held orders; the fraud rules read real data; P-11 (xmin on deductions and adjustments). |
| P7 | PostgreSQL proofs (concurrent sendings, concurrent decisions, the atomic posting, the regime blocker per employee, P-11's race) and the migration as a non-superuser owner under forced row security; the HTTP classes that used one token with conflicting roles now use one person with two tokens. |
| P8 | AC-PAY-58a..h; AC-24 and AC-25 rewritten; the other classes meet the second approval and the review by API (the SQL fixture deleted); CI scope and ownership; `scripts/pay058-validate.ps1`; a reference check of the new tests and the independent review, each fix with a test. |
| P9 | `origin/main` did not move before P10. At P10, on the owner's word, PR #58 (GHCR release images) was merged and `main` merged into the branch (one doc conflict, both sides kept). |
| P10 | The demo server through the GHCR path (§13). |
| P11 | Docs, the local gates, this report, the PR and its merge (§12, §16). |

## 2. Commits

| Commit | Subject |
|---|---|
| `774f264e` | docs(pay058): PLAN-58's agent prompt |
| `c5882596` | chore(pay058): baseline and inventory |
| `d6df238a` | feat(pay058): money leaves by one path |
| `338c4b5d` | feat(pay058): one bank file per sending |
| `7189c40a` | feat(pay058): every four-eyes act is a request and a decision by two people |
| `8046cd81` | feat(pay058): two approvers above the threshold, and the auditor's review |
| `ce78331e` | feat(pay058): conflicting roles refused and audited |
| `917f8d68` | feat(pay058): holds per employee; the fraud rules read real data |
| `2c2999d2` | test(pay058): PostgreSQL proofs |
| `84989574` | test(pay058): AC-PAY-58a..h, AC-24 and AC-25; review fixes; CI scope and ownership |
| `f4afd0aa` | merge of `origin/main` (PR #58, GHCR release images) |
| later | the server checks class, docs and this report (see `git log`) |

## 3. Decisions applied and deviations

- **The second auditor.** The review is opened by one `auditor` and closed by another (`acc-auditor`, `acc-a-auditor-2`); neither made,
  approved or posts the run; a reviewer of the present version does not approve it; only the reviews opened after the run's last
  rejection count (review fixes M2, M3).
- **The lifted refusal.** A service-regime blocker no longer refuses a calculation by default (`HoldEmployee`); `BlockRun` keeps the old
  refusal, now **before** `BeginCalculating` (the run is left as it was, no job).
- **The re-pinning exception.** Verifying an employee's new account re-pins his unsent `Draft`/`Held` orders and their lines, in the
  verification's transaction, with an `EntityAuditLog` line holding ids only. It is the one exception to D-PAY-15.
- **`ArrearsSettlementService`** keeps its role for the settlement of arrears; arrears of a funded batch that never left are voided
  with its run's reversal (`ArrearsLedgerStatus.Voided`), and a voided row is owed to nobody.
- **P-11.** `DeductionRecord` and `PayrollAdjustment` carry `xmin`; a calculation that loses the race fails its partition with an
  Arabic message and writes nothing of its batch.
- **Requests on their targets' routes** (release, resolution, adjustment, settlement, rejection, reissue) with one decision route,
  instead of one generic request route.
- **FR-04's action is `payment-hold`** (was `flag-for-review`), so AC-PAY-58g holds the two orders; **FR-07 reads the employee's first pay
  profile**, so AC-25's old fixture no longer looked like a new hire, and AC-25 proves the fraud hold by FR-04.
- **The run route of the funding decision** stays as an alias (400 for a run not posted).
- **The funding allocation is now in a fixed order** (employee id, then order id): found by the reference check, it read the orders
  with no `ORDER BY`.
- **Acceptance data.** No seed gives an employee a bank account (an order without one is held `NO-ACCOUNT`), so each sending class first
  gives a few of its own employees a verified account by PLAN-55's two people.
- **The demo's limits.** It has no poster, second approver or auditor, and adds none (P-16): nothing is posted there. A calculation
  there now succeeds and holds every one of its 425 unclassified employees `REGIME-BLOCKER`.
- **The server path.** At P10 the demo ran the owner's unmerged GHCR release; P-17 forbade deploying over it. The owner answered
  «ادمجه واكمل»: PR #58 was merged (all 15 checks green), `main` merged here, and the images were built on GitHub Actions — which needs
  the code on GitHub, so the release branch was pushed before the server checks; what came after it is docs and this report only.
- **No deviation defers part of the plan.**

## 4. The one path

- **What makes a batch:** `PostPayrollRunHandler` → `PaymentBatchIssuer.IssueAsync`, in the posting's transaction: one
  `Disbursement` batch (`Source = PayrollRunPosted`) and one order per line with a positive net, pinned to the line's account, held
  `NO-ACCOUNT` when the line pinned none. `PaymentBatch.Seeded` only in the demo seeder.
- **What sends:** `ProcessPaymentBatchHandler` (first sending) and `SubmitHeldPaymentOrdersHandler` (released orders), by
  `Payroll.Post`, never the run's creator, approvers or funding decider; each runs the fraud scan and the gate per employee in the same
  request, moves the eligible orders to `Submitted` (only `PaymentOrderCoordinator` writes that status — an architecture rule) and
  writes one file.
- **The sum rule after every act:** Σ non-cancelled orders + `ArrearsAmount` = Σ positive line nets (`PaymentBatchLedger`).
- **Removed:** `POST runs/{id}/partial-disburse`, `ExecutePartialDisbursementCommand`, `ExportTreasuryFile`, the external bundle, the
  disbursement gateway, and `PaymentOrderService.TryAdvanceStatusAsync` (no caller; a latent second sender).

## 5. The bank file

CSV, UTF-8 without a byte order mark: a header, `PaymentOrderId,Amount` per sent order, and `CONTROL,<count>,<total>,<batch>,<sequence>,<kind>`.
`Sequence` is unique per batch (`IX_PaymentBatchFiles_Batch_Sequence`), so two concurrent sendings make one file. Stored with its SHA-256
and content, downloaded as stored (a bulk-export security audit line naming the file, never an account). A reissue is four eyes and
supersedes the file with a new sequence; a file whose orders were acknowledged, settled or rejected is not reissued (review fix M1).

## 6. Four eyes

| Kind | Asked by (policy) | Decided by | Effect |
|---|---|---|---|
| HoldRelease | Payroll.Approve | another Payroll.Approve | the hold released (and its linked alert resolved); a REGIME-BLOCKER only once HR.Core no longer blocks |
| CriticalAlertResolve | Payroll.Approve | another | the alert resolved or dismissed, with its hold |
| ArrearsAdjust | Payroll.Process | Payroll.Approve | the row's amounts |
| BankFileReissue | Payroll.Post | Payroll.Approve | a new file superseding the old |
| BatchSettle / BatchReject | Payroll.Post | Payroll.Approve | the batch and its acknowledged orders `Settled` / `Rejected` |

The requester never decides (400); the decider is the caller from his token; a request expires after 72 h; one pending request per
kind and target; the effect happens in the decision's transaction; the request row's `xmin` makes two concurrent decisions one.
The channel's acknowledgement is the poster's single act, with the channel's reference, in its own transaction.

## 7. The approvals and the review

`PayrollRun.Approve` counts different approvers of the run's present version (`Revision`); the required count comes from
`SeparationOfDutiesService` with the agency's threshold. The creator never approves; an approver never counts twice; a reviewer of the
present version never approves. The posting needs a closed review of the present version (unless `Payroll:Audit:Required=false`,
written in the run's log), and the poster is none of its approvers or reviewers. The gateway lets `Payroll.AuditReview` reach the
review's paths only (`GatewayPayrollReviewPaths`, `GatewayAuditorReachTests`).

## 8. Conflicting roles

`PayrollRoleConflictRequirement` on `Payroll.Process`, `.Approve`, `.Post`, `.Reverse`, `.AuditReview` (and what reads them, such as
`Payroll.LegalAttest`): a token holding a pair of `SodMatrixDefaults` is refused, 403 with the pair named (JSON on the API, an Arabic
page on a screen), and the act's own authorization writes a `RoleConflict` security audit line (no token). `hr-admin` counts as
processor and approver everywhere and as poster where `Payroll.Post` keeps him (demo, lab). `Payroll:Sod:RejectConflictingRoleHolders`
(default true) only stops the refusal, never the audit. The realm import refuses a conflicting persona unless marked
`sodConflictFixture` (`scripts/keycloak/persona-role-conflicts.ps1`); the one fixture is `acc-a-payroll-multirole`.

## 9. Holds per employee

The gate holds an employee's order for: an active hold of the run, or with no run (every batch); an open critical alert of his; no
pinned account (`NO-ACCOUNT`); a retired pinned account; a service-regime blocker (`REGIME-BLOCKER`). Only a critical alert with no
employee stops a batch. The held report (`GET payment-batches/{id}/held` and the batch's page) names each held employee, the reason,
the amount and any pending release. Fraud inputs: FR-01 from PLAN-57's attendance receipts, FR-04 from pinned accounts, FR-05 from the
last posted regular net, FR-07 from the first pay profile, FR-08 from input snapshots (a correction carries its source). One alert per
run, employee and rule, whatever its status (review fix L2).

## 10. Existing tests that changed, and the numbers that did not move

- **Unit:** `Frd001FraudEngineTests` (FR-04 now holds both employees), `Syr007RegimeGovernanceTests` (both modes), `Pay055*` harness (the
  verification's transaction on the in-memory store), `Pay001ApprovalBypassTests` (the issuer), approval signature tests.
- **Integration:** the PLAN-55/56/54/53 HTTP classes give one person two tokens where one token held conflicting roles (each refusal
  keeps its reason); `Pay055TestAccounts.Iban` hashes the whole id (sequential ids shared one IBAN, which FR-04 now holds);
  `Pay056RunLifecycleHttpTests` posts with the antiforgery token.
- **Acceptance:** AC-24, AC-25 rewritten; AC-PAY-56 and AC-12 meet the second approval and the review; AC-PAY-54b and 55a: hr-admin's acts
  are 403 with the conflict named (the editor and self-verification rules stay proved by the HTTP integration classes); AC-UX-67 probes
  the new pages; the V2 catalog exempts the one fixture.
- **What did not move:** every existing payroll result — the golden packs, `PayrollEnginePipelineTests`, `Pay023`, `Pay029*`, `Pay056*`,
  `Lva057*` pass unchanged; the migration changes no stored amount (checksum unchanged in `Pay058MigrationPostgreSqlTests`).

## 11. The guards and their negative tests

`Pay058SinglePaymentPathArchitectureTests` (batch makers, the issuer's caller, the senders, the Submitted write, the confirmation
commands, the removed paths; each with a planted case), `PayrollWriteActionsTests` (every new write's policy and reach),
`GatewayAuditorReachTests`, `persona-role-conflicts.ps1 -SelfTest`, `scripts/pay058-validate.ps1 -SelfTest` (22 planted defects).

## 12. Results

- Unit filters 1,052; architecture 444; payroll integration on SQLite all green (209 + the review fixes' 5); the full Testcontainers step
  (CI's filter) 334 passed, 1 skipped.
- Acceptance, local fresh stacks: AC-PAY-58 8/8 (twice); AC-PAY-54/55/56, AC-IAM-50..53, AC-LVA-57, AC-HCM-25 55/55; AC-UX-67 7/7;
  isolated 46/47 (AC-37b needs the E2E journey, CI-only); lab AC-21, 23, 24, 25, V2 catalog, AC-HCM-29 green, AC-12 Blocked (PLAN-59,
  as on main); AC-OPS-38 8/8; AC-TEN-48 502 twice (first test at the stack's start, before HR.Core was ready). Seeded counts 64/64.
- **The evidence of record is the PR run's artifact** (link in §16 once the run is green).

## 13. The server

*Filled in after the deploy: before and after, the change table and its undo, the backup, the checks, the rollback note.*

## 14. Changes for API clients and operators

- **New:** `POST payment-batches/{id}/funding-decisions|process|submit-held|ack|settlement-requests|rejection-requests`,
  `files/{fileId}/reissue-requests`, `GET payment-batches/{id}|/held|/files|/files/{fileId}`; `GET dual-control`, `dual-control/{id}`,
  `POST dual-control/{id}/decide`; `POST fraud-alerts/{id}/resolution-requests`, `arrears/{id}/adjustment-requests`;
  `POST runs/{id}/audit-records`, `audit-records/{id}/close`. Every write validates the antiforgery token.
- **Changed:** `payment-holds/{id}/release` asks for a release (202 with the request); the body's approver is gone. The run's `approve`,
  `post` and `fraud-scan` now validate the antiforgery token; `approve` answers the approvals given and required.
- **Removed:** `POST runs/{id}/partial-disburse`, the treasury file and bundle exports.
- **Settings:** `Payroll:Approval:DualApprovalThreshold`, `Payroll:Audit:Required|ReviewerRoles`, `Payroll:Blockers:Mode`,
  `Payroll:Sod:RejectConflictingRoleHolders`, `Payroll:DualControl:ExpiryHours`.
- **Personas:** `acc-a-auditor-2`; the fixture `acc-a-payroll-multirole` (sodConflictFixture); the import guard.

## 15. Items other plans own, and defects for later

- PLAN-59: the full cycle on a production-like agency (a poster, two approvers, two auditors), and AC-12's lab block.
- PAY-I (wave 2): sending to the bank channel and reading its answers automatically; per-order partial settlement.
- Data task: classify the demo's service regimes (every calculation there holds all 425 employees).
- Recorded, not changed: `Payroll.RunCancel` is not one of the five financial policies; holds of a cancelled run stay `Active` but act on
  nothing; the gate is read before the sending's transaction (a hold made in that window is caught by the next sending).

## 16. Timing

*Filled in at the end (total, active, CI waits, every stop, build/acceptance/deploy times, Actions minutes, `plan-timing.ps1`).*

## 17. Readiness note

- **PLAN-59:** a full cycle runs through the one path: calculate, submit, approve (twice above the threshold), review (two auditors),
  post, fund if partial, process (and submit-held), ack, settle by four eyes. The gate settings above; the people a posting needs: a
  processor, one or two approvers, two auditors, a poster.
- **PLAN-64:** a final settlement posts through the same issuer (`PaymentBatchIssuer.IssueAsync` from the posting), and `Settled` is «paid».
- **PAY-I:** what the manual confirmation leaves to automate: the channel's ack and statement matching per order, and the reissue on a
  channel's refusal.
