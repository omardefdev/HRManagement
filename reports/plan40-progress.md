Started (UTC): 2026-11-02T08:01:20.0119751Z

# PLAN-40 progress

## P0
- status: in progress
- Preconditions (08:02Z): PLAN-39 PR #34 was open with all checks green -> `gh pr checks 34 --watch --fail-fast` exit 0 ->
  merged by the agent at 08:02:07Z (merge 7e23094f); post-merge run: dedupe guard skipped CI/CodeQL/E2E/Acceptance.
  main = origin/main = 7e23094f. ssh hrm-vps whoami = hrm; ED25519 host key SHA256:qWIfQls8...pUMGsto (known_hosts).
- Branch feature/plan-40-msg-atomic-event-publishing from 7e23094f. Commits: 9cd28dd3 prompt, 044be937 PLAN-39 notes,
  22f9d2f1 P-5 carry-over (fresh plain stack up --wait 18 s, HR.Core + File.Storage ready, restart 0).
- Baseline build -warnaserror: 0/0, 61 s. Filters (OutboxAtomicity|InboxIdempotency|Qa003Outbox|RabbitDownOutbox|Int001|Int002):
  UnitTests none match; IntegrationTests 14 pass + 5 skipped without RUN_TESTCONTAINERS; with RUN_TESTCONTAINERS=true
  18/19 — RabbitDownOutboxTests (Chaos) fails "Outbox did not drain": restarting the Testcontainers broker gives it a new
  host port while the bus keeps the old one (pre-existing, environment; to fix in P3/P6 since Chaos is in the Verification).

### Design notes (read before P3)
- MassTransit 8.5.10 consumer outbox (EntityFrameworkOutboxContextFactory): TWO transactions per message — pass 1 inserts
  InboxState + SaveChanges + commit (consumer not run), pass 2 runs the consumer, SaveChanges, commit, then delivers.
  The outbox filter sits on the consumer/saga MESSAGE pipe (inside the bus-level InboxConsumeFilter).
  => the plan's "stage the receipt before next.Send" would commit the receipt in pass 1 before the consumer runs (a failed
  consumer would then be skipped as a duplicate on retry). Decision: join mode = the bus-level filter passes through; the
  receipt is checked and staged by a filter added on the consumer/saga message pipe AFTER UseEntityFrameworkOutbox
  (inside the outbox, pass 2), so it commits with the business state and OutboxMessage rows. Prove in Msg040 test (P-1).
- CORRECTED: InboxState is already in the 8 outbox models (MassTransit maps OutboxMessage -> InboxState FK), tables exist on
  the server; only the five new outboxes (+ Training receipts) need MSG040 migrations, as P-3 says.
- EF saga repo (ExistingDbContext, pessimistic) joins the outbox transaction via the DbTransactionContext payload.
- No consumer calls BeginTransaction directly; services called from consumers already run under the inbox transaction today.
- 0.3 inventory: two read-only subagents (~6-7.5 min each, in parallel) -> reports/plan40-publisher-inventory.md
  (A 10, B 39, C 56, NO-OUTBOX 2, OK 31, dead 4). Roslyn scan deferred to P4 (the guard re-checks it) - deviation recorded.
- 0.4: fresh prod-like stack up 18 s, all ready 40 s; AC-MSG-40a run on PLAN-39 images: 302, Executed, outbox rows 0->0,
  receipts 0, note 0 (artifacts/plan40/baseline-04-db-checks-40a.json). Evidence folder removed (rerun in P6).
- Server before: tag 817304f5bd88, 22 containers, outbox tables in the 8 outbox DBs only, 14 mq_* connections only,
  queues empty except hr-contracts-web-transaction-archived_error = 3.
- Acceptance runner for this session: artifacts/plan40/run-acc.ps1 (sets the run-all env; PS 5.1).
- P0 Finished (UTC): 2026-11-02T08:22:59Z

## P1
- status: in progress
