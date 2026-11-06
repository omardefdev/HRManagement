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
