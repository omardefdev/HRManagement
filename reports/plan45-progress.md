Started (UTC): 2026-11-07T13:55:29.4555115Z

# PLAN-45 progress

Branch feature/plan-45-wf-asset-custody-approval-loop, from origin/main aeb6d716, main folder, one plan at a time.

## P0 - baseline and inventories
Finished (UTC): 2026-11-07T14:06:11.2927772Z
- Preconditions: all five hold. Host key matches the plan. Server tag 973d68488f09, on origin/main.
- Notes: eaffc6c6 cherry-picked as 53f5fd34. 5a3ce027 was empty here: PLAN-44's baseline commit already carried it (git diff against HEAD is empty). Both note branches deleted with -D, because -d refuses a cherry-picked branch; content verified identical first. Old tips: 5a3ce027, eaffc6c6.
- PLAN-66's three notes and the prompt file: ab9cda16.
- Inventories: two read-only subagents, about 8 min, beside the baseline. Build 57 s. Unit filter 10 s, integration filter 16 s.
- Decisions: tenant source for the job = AuditLogs of admin_db narrowed to the two entity types (reports/plan45-baseline.md). No Stop.
- Test results: see reports/plan45-baseline.md.
- Server changes: none.
- Next step: finish P1 (AdminLogistics loop).

## P1 - the loop in AdminLogistics
Finished (UTC): 2026-11-07T14:17:09.1345985Z
- Migration WF045_AssetApprovalLoop: the fields, xmin on AssetLossReport (Npgsql emits nothing for it), IdempotencyRecords (so [Idempotent] has a table), an index on AuditLogs (EntityType, TenantId), the loss report's unique index widened to Status IN (0, 4) so ApprovalStarting blocks a second report. No data change.
- Decisions: WorkflowTriggerService deleted, AssetApprovalStarter replaces it; AssetApprovalLoop holds the start, the retry and the withdrawal; RequestedByName added (the job otherwise starts instances with no requester name); the starter sets X-Acting-User to CreatedBy, so a retry by a colleague keeps the real requester.
- Withdrawing a request in ApprovalStarting looks up the key first, to cancel an instance a lost answer left behind. With HR.Core down the asset is still freed.
- PartyAck compensation now reopens Returned rows only (it would have reopened withdrawn and rejected ones).
- Wiring: hr-core.workflow-instances.cancel added.
- Tests: Wf045 rules, architecture guard, loop, starter, job. Filter Wf045|Wf043|Fun005|Asset|HrCoreEndpoint|Data001|Ten002|ProductionWiring: 402 passed, 1 failed (the QuestPDF notice from the baseline).
- Next step: P2.

## P2 - the chain
Finished (UTC): (commit 5348fb37, before P3; stamped late in this section's file order)
- EnsureCustodyChainAsync replaces EnsureMultiStepAssetAssignmentAsync: raises both codes, leaves a later version with the same roles alone, retires an older published version, never publishes a first one.
- Guardrails: FirstStepRole and MaxSteps added. ExternalApprovalGate, one per registered type. admin-logistics in WorkflowStepAction and HrCoreWorkInbox only. AssetLossReport exemption closed.

## P3 - generic page and inbox
Finished (UTC): (commit cb5f09ac)
- WorkflowApprovalsController, WorkInboxAudience, the gateway rule placed before the mutating block, the module filter. The deep link is relative to HR.Core. Branch-level holders read inside their unit only (addition to the plan). A short menu for a logistics-only user.

## P4 - tests and repair
Finished (UTC): 2026-11-07T14:47:37.4893640Z
- L3 closed: ConcurrencyExtensions.IsDeadlock, WorkflowConcurrency.LostRace used by decisions, cancels and SaveOrConflictAsync, and ConcurrencyConflictMiddleware as the net.
- Wf045AssetApprovalPostgresTests: 8 of 8 (outbox, withdraw race x5, real deadlock, indexes, freed index, xmin, retry by key, job under forced row security as a non-superuser).
- Integration Wf045|Ac33|Wf043|Wf044: green after one test was updated for the loss code's distinct approvers. Unit: 5370 passed, the 1 failure fixed in the commit.
- AC-11, AC-33, AC-WF-43e/f rewritten; AcWf45 written; ci-scope and ownership updated early, because the guards need them.
- Next step: P5.

Stamps for P2 and P3, from their commits (the sections above were written later): P2 finished 2026-11-07T14:27:00Z, P3 finished 2026-11-07T14:31:11Z.

## P5 - acceptance and review (running)
- Job 1, prod-like, AcWf45 only: stack 897 s with images; AC-WF-45a..h 8 of 8, 70 s.
- Repair script on the acceptance data, all modes run: dry, -Apply (the job linked the row), -RestartOnLatest (old instance Cancelled, new one on v2). The run showed a defect in the script's count (char cast), fixed.
- Pending requests on the acceptance data before the repair: 0 without an instance; the one found was inserted for the check.
- Job 2, prod-like regressions, -NoBuild: stack 264 s. AcHcm25 and AcWf43 14 of 14, AcMsg40 8, AcWf44 5, AcOps39 8, AcS2s42 9, AcInt37 6 of 8. Ac37b and Ac37e are Blocked: they need the journey stage, which a -Classes run skips (recorded before; not a regression).
- Independent review (one subagent, read-only): 1 high, 1 medium, 5 low. H1 checked against the code and confirmed: my change to CompensateAsync (Status == Returned) could not match what ExecuteAsync closes. Fixed: Active, PendingApproval, ApprovalStarting; a test covers it. M1 fixed: Path.Value in the new log line. L1 fixed: ApplyApproved refuses a request closed by a transfer or separation. L2 recorded in the baseline. L3, L4, L5: see the final report.
- The row-security test now seeds through the app role and the real audit interceptor.
- Unit after the fixes: 5373 of 5373. Wf045 PostgreSQL: 8 of 8.
- Job 3, -Profile Both after the review fixes, images rebuilt: lab stack 905 s, prod-like 269 s. Ac11, Ac32, Ac33, AcWf23, AcEst24, AcHcm28: 16 of 16. AcWf43 6 of 6, AcMsg40 8 of 8, AcWf45 8 of 8. Guard PASS (9 classes in 4 trx files), job PASS.
- Demo users added to the realm manifest: e2e-logistics-maker, e2e-logistics-checker. AcWf45VpsChecks written; AC-WF-43's server check moved to 3 Retired and both custody codes.
- Stack down with -v. Evidence restored, a copy in artifacts/plan45/acceptance.
Finished (UTC): 2026-11-07T15:50:01.0044594Z
- Next step: P6, then P7.

## P6 - main
origin/main did not move (aeb6d716): nothing to do. Finished with P5.

## P7 - the demo server
Finished (UTC): 2026-11-07T16:57:31.2619808Z
- Condition: running tag 973d68488f09 is on origin/main.
- Backup /opt/hrm/backups/pre-plan45-20261107T155048Z verified: SHA256SUMS OK, 17 CREATE DATABASE. Undo: restore only if data is damaged.
- Build of 9560b03e51a3, detached, 1280 s, 17 of 17 tagged. Deploy 150 s, smoke 50 pass, 0 fail, 1 warn. Undo: scripts/vps/deploy.ps1 -Rollback (to 973d68488f09).
- Demo users e2e-logistics-maker, e2e-logistics-checker through scripts/vps/add-demo-user.sh. Undo: Keycloak admin, delete both. First attempt lost tenant_id (user profile); fixed, the profile restored.
- Repair: dry and -Apply, 0 items.
- AC_WF_45_vps 3 of 3. Server suites: 12 pass, 9 fail, 6 skip on ssh errors; rerun 13 of 13 and 6 of 6.
- Server keeps assets named PLAN-45 server check.
- Next step: P8.

## P8 - docs, gates, merge
- Local CI on 8957cc32: 91 passed, 0 failed, 1629 s, after two failed runs (byte order mark; Ops038 plugin download).
- Final report and PR text written.
- Next step: pre-push check, release/plan-45, PR, wait for the checks, merge only when all are green.
