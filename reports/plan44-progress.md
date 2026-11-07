Started (UTC): 2026-11-06T18:11:31.6651329Z

# PLAN-44 progress

Mode: normal. Clone: primary (`D:\my projects\HRManagement`). Base: `origin/main` f8f5cd04. Branch: `feature/plan-44-wf-instance-integrity`.
PLAN-66 runs beside this in `D:\my projects\HRManagement-b` and held `acceptance-stack` from 18:07Z.
PLAN-43's merge note rides in the first commit, from `docs/plan-43-merge-note`.

## Locks
(none yet)

## Server changes
(none yet)

## Phases
P0-P4 stamps below come from commit times and were written late, at the start of P5 (no stamp was edited).
- 9b0b0790 2026-11-06T21:20:28+03:00 chore(wf044): baseline, duplicates and stuck outcomes on the acceptance d
- 7554cf42 2026-11-06T21:30:40+03:00 feat(wf044): workflow instances carry an xmin token; a concurrent decisio
- d6d1f4f1 2026-11-06T21:34:59+03:00 fix(wf044): the decision's unit of work sits in ApproveAsync and RejectAs
- d4df6578 2026-11-06T21:45:38+03:00 feat(wf044): at most one running instance per entity and per idempotency 
- d9926451 2026-11-06T21:53:25+03:00 feat(wf044): a decision, its effect and its outbox row commit together; a
- fa92c468 2026-11-06T22:15:41+03:00 feat(wf044): the gates and the inbox bar the same makers from every step;

## Notes so far
- 7554cf42 was committed with OutboxPublishOrderingArchitectureTests red: my command chained the commit after the tests. Fixed in d6d1f4f1, before any push. Later commits are gated on the results.
- Unit suite on fa92c468: 5107 of 5107. Build -warnaserror: 0, 0.
- Waits: acceptance-stack held by PLAN-66 until about 19:09Z; no time lost, P1-P4 ran beside it.

## Locks
- acceptance-stack acquired 2026-11-06T19:41:46Z (free since about 19:09Z; PLAN-66's local CI used Docker from about 19:07Z, and Testcontainers waited for it under P-2).

## P5
- WAITING-LOCK (Docker, P-2): about 19:22Z to the waiter's exit; recorded as a wait, not active time.
- Wf044InstanceIntegrityPostgresTests 14 of 14; wider integration filter 102 of 102, 263 s.
- Acceptance before-picture: no hrm-acceptance volumes, so no data before the stack migrates.
- First prod-like job (5f636710), 1299 s: AC-WF-44a,b,c,e Pass; AC-HCM-25 + AC-WF-43 14 of 14; AC-MSG-40 8 of 8; AC-WF-44d failed and showed a real defect.
- Defect: Approvals/Approve and Reject fell to HrCoreWrite at the gateway, so a pure hr-management-approver could not reach legacy step 2; with the new role check a two-step legacy request could never finish. Fixed: the gateway applies HrCoreApprove, the controller's policy. AC-WF-44d now finishes a two-step request.
- Second job (ec5bdd1b, -Profile Both, 17 classes), 2925 s: prod-like AC-WF-44 5 of 5, AC-HCM-25 + AC-WF-43 14 of 14, AC-MSG-40 8 of 8; lab 47 of 48.
- Ac29_e failed twice, differently: a connection reset on a GET, then the accept stuck 16.6 s in the upload to file-storage-web (499, gateway 405), repeated 80 s later in 0.6 s. file-storage-web logged thread-pool starvation at 21:04:00; GitHub and ssh were unreachable from the host at about 21:07Z and back by 21:08Z. Offer instance Completed and offer Issued: the decision path worked. Under investigation by rerun.
- Duplicates report on the prod-like stack after the run: duplicates 0, stuck 0, row security forced, 15 instance groups. Metrics exported; job log: 4 tenants, 0 items.
- AcHcm29 rerun on the same images: 3 of 3 in 37 s, guard PASS (377 s). Every required class of the P-10 list has passed locally; Ac29_e failed twice during the host stall and passed once healthy, and the CI run is the evidence of record.
- Group: AcWf44InstanceIntegrityTests in prodLike isolated, and it also removes what it seeds.
- acceptance-stack: stack reset at 21:16Z; the first release call failed (no -Plan), so the lock was really released at 2026-11-06T21:16:22Z.
Finished P5 (UTC): 2026-11-06T21:16Z

## P6
origin/main still f8f5cd04 at 21:08Z: nothing to move.

## Review (P8 step 2, run before P7 so the server builds once, on the final code)
One independent review of origin/main...HEAD: 1 high, 2 medium, 6 low. H1 checked against Review.cshtml by hand and confirmed, so no refuter.
- H1 fixed: the gateway rule missed the path the Review form really posts (no id suffix); AC-WF-44d now posts the rendered form URL.
- M1 fixed (appeal start under Enforce), M2 fixed (an upheld objection is left to the operator), L1, L2, L4, L5, L6 fixed.
- L3 recorded, not fixed: a cancel and a decision can deadlock on lock order; both roll back, the loser gets a 500.
- L6 second half recorded: on the previous tag a sequential second start now gets a unique violation, not a second instance.
- Unit 5115 of 5115; Wf044, Wf043, Hcm031 integration 38 passed, 1 skipped. SensitiveDataAuditTests failed once in a parallel run, passed alone and in the next full run; not this change.
Resumed (UTC): 2026-11-07T08:22:47Z reason: the server build's ssh session was reset and the host lost its network for long stretches; the demo-server lock expired at 02:14Z.

## P7 (demo server)
- demo-server: acquired 22:14:17Z, expired 02:14Z during the outage, taken over 08:23Z (own lock), released 08:53Z. Check of P-13: running tag 3bd9ee92bcf5 on origin/main.
- origin/main moved to f77dd28b (PLAN-66, PR 39): merged at 973d6848, no conflict, no service code; unit 5244 of 5244.
- WAITING (host network): GitHub and ssh lost at about 22:26Z; the first build died before its push; the host was back by 08:22Z.
- Backups: pre-plan44-20261106T221735Z and pre-plan44-20261107T083208Z (700/600, SHA256SUMS OK, 17 CREATE DATABASE lines).
- Build: first run compiled 17 images, then lost; rerun detached with nohup 08:23Z to about 08:31Z, rc=0, 17 of 17 in the registry.
- Deploy 973d68488f09: 147 s, no keycloak-reconcile failure, smoke 50 pass, 0 fail, 1 warn; previous-tag 3bd9ee92bcf5.
- Checks: duplicates 0, stuck 0, row security forced; AC_WF_44_vps 3 of 3; AcOps36 (36j excepted), AcInt37, PLAN-38..43 server checks 21 of 21, 997 s. Dry run 0 items, so nothing to apply.
Finished P7 (UTC): 2026-11-07T08:53Z

## P8
- Local CI on afb0922b: 91 passed, 0 failed, 1329 s. Pre-push: OK, 793 s.
- PR: https://github.com/omardefdev/HRManagement/pull/41, opened 2026-11-07T10:47Z from release/plan-44 (3e02afd6).
- WAITING-CI: PR run 42365420730 from 2026-11-07T10:47:21Z to 11:30:27Z, 43 min. The turn ended on WAITING-CI at about 11:02Z; the next check came on the owner's message at 13:23:52Z, so green to merge was 1 h 54.

## Merge note
- PR: https://github.com/omardefdev/HRManagement/pull/41, head 3e02afd6, merged at 2026-11-07T13:24:11Z.
- Merge commit: aeb6d716758d3928d28651df049efee6205f195f. main = origin/main = aeb6d716 after git pull --ff-only.
- PR run: https://github.com/omardefdev/HRManagement/actions/runs/42365420730, success, 14 of 14 checks, no rerun:
  - Gitleaks pass 1m2s; Reuse check pass 22s; Dedupe post-merge run pass 8s
  - CI pass 24m12s; CodeQL pass 16m35s; E2E pass 15m33s; Acceptance images pass 15m39s
  - Acceptance stage: leastpriv pass 8m20s; meta pass 11m55s; prodlike pass 16m57s; lab-rest pass 17m24s; lab-main pass 24m37s; prodlike-final pass 26m18s
  - Acceptance pass 29s
- Evidence of record: the run's artifacts, https://github.com/omardefdev/HRManagement/actions/runs/42365420730#artifacts (acceptance-evidence, the six acceptance-stage-* sets, test-results, e2e-results, codeql-sarif, gitleaks-report). No evidence committed.
- Actions job time: 10771 s, about 180 min across 14 jobs; 43 min wall-clock.
- Demo server after the merge: 16 images on 973d68488f09, 0 unhealthy, / answers 302. From 973d6848 to aeb6d716 only tests, reports and docs changed, so no new deploy is needed.
- Total: 2026-11-06T18:11:31Z to 2026-11-07T13:24:11Z, 19 h 13 wall-clock: about 6 h 40 active, 9 h 56 host outage, 2 h 37 from PR opened to merge (43 min CI, 1 h 54 waiting for the next message). PLAN-43 wait: none. Lock wait: none of its own; a short Docker wait behind PLAN-66 from about 19:22Z, inside other work.
- Open for later plans: L3 (a cancel and a decision can deadlock, the loser gets a 500, no data damage), and PLAN-66's three uncommitted files in D:/my projects/HRManagement-b.
- Post-merge run: https://github.com/omardefdev/HRManagement/actions/runs/42376802623 on aeb6d716, success at 13:25:18Z. Gitleaks ran; Dedupe post-merge run passed and the other jobs were skipped, because the tree was already tested in the PR run.
Merged by agent on owner instruction (UTC): 2026-11-07T13:24:11Z
