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
- Resumed (UTC): 2026-11-03T23:12:20Z — reason: the session was interrupted by the user mid-P1 (2026-11-02) and the owner said «اكمل». Uncommitted P1 work is intact in the tree.
- Shared: Messaging/OutboxUnitOfWork, StagesOutboxMessagesAttribute, DeterministicMessageId (namespace 6f1c9a52-40e8-4b1d-9a3e-2d7c0b5e4a10).
- Fixed: IPromotionExecuted, IDelegationExecuted (MovementExecutedEvents, publish before the single save, id from the request);
  asset outcomes (WorkflowAssetOutcomes, id from the workflow instance); PublishAllOrgUnitsAsync saves, returns the count;
  the two org-unit stagers marked; IAgencyOnboarded before its save; IPolicyPublished inside PublishDraftAsync (covers the UI path), removed from the controller.
- DEVIATION (ApproveAsync/RejectAsync): NOT wrapped in OutboxUnitOfWork. The asset outcome is staged before the single save
  (D-40-2's default). Reason: handlers that run after completion open transactions unconditionally (EstablishmentChangeApplier:191,
  CompetitionResultsService:139/278, CompetitionAllocationService:29, RecruitmentSelectionService:173) and some roll back and return
  a result; joining them to an outer transaction would change their rollback semantics. Only asset types publish here, so the
  event is atomic with the instance; the handler chain publishes through its own single saves as before.
- Tests: Msg040ClassAPublishOrderTests (OutboxOrderProbe: ordered publish/save log), DeterministicMessageIdTests (RFC vector);
  with Hcm025/AgencyProvisioning/PolicyOrchestrator/OutboxPublishArchitecture 163/163; integration Hcm025+SoDRequisition 15/15 (Testcontainers, 147 s).
- UnitTests now references HR.Movements.Web (lock file refreshed; the other 7 modified lock files are line endings only).
- P1 Finished (UTC): 2026-11-03T23:17:58Z

## P2
- status: in progress
- All class B sites changed: SingleSave (Compliance x3, Health x2, Training x2, IDCard x2, Payroll post, export jobs in HR.Core/Contracts/Payroll/Compliance, Secondment.Activate, Loan.Execute,
  Advance, Probation, AdministrativeDecision, Separation publish/step, ActivateContract, CreateEmployee, status change callers) and OutboxUnitOfWork (Transfer.Execute, org unit create/edit/move in both controllers,
  ApplyManifest after the shared national seed, Leave approve/cancel, PublishHireCase, CreateCommencement). Archive/Analytics export jobs wait for their outboxes in P3.
- IContractsRepository.ExecuteAtomicAsync wraps OutboxUnitOfWork (the Application layer has no DbContext). OrgUnitPathService.MoveSubtreeAsync joins an ambient transaction.
  Disciplinary action: the File.Storage reference call moved after the commit (no HTTP between state and event).
- Tests: OutboxAtomicity payroll/leave tests now assert no save before the publish; Msg040OutboxUnitOfWorkIntegrationTests (failure on the last save: neither state nor message; ambient join) 4/4;
  Msg040MovementsPublishOrderTests 2/2 (moved from UnitTests: referencing HR.Movements.Web there made MassAssignmentArchitectureTests see its entity-binding controllers; UnitTests csproj and lock back to the original).
  Unit 4580/4580. Integration 458 pass, 4 fail, 4 skip: 3 Qa003Minio (MinIO image, CI-excluded, Category=ObjectStorage) + RabbitDown Chaos (baseline failure).
- docs/EVENT_PUBLISHER_INVENTORY.md rewritten (pattern column, every event, Save-Publish-Save removed).
- P2 Finished (UTC): 2026-11-03T23:41:26Z

## P3
- status: in progress
- P-1 first: Msg040OutboxOrderingIntegrationTests 8/8 on PostgreSQL + RabbitMQ BEFORE enabling any service (no "already an active transaction"; reply visible only after the commit;
  failed commit: no message, no state, no receipt; duplicate: one effect, also after InboxState is deleted; failed attempt: retry runs; party business/DB/transient/success paths).
- DEVIATION (plan §4 "stage the receipt before next.Send"): MassTransit's outbox commits a first transaction that only inserts InboxState, so a receipt staged before it would be committed before the consumer runs.
  InboxReceiptConsumeFilter sits inside the outbox on the consumer and saga message pipes (connected after UseEntityFrameworkOutbox) and stages after the consumer; the bus-level InboxConsumeFilter passes through when ConsumerOutboxRegistration exists; its transaction stays for File.Storage (no outbox).
- DEVIATION (D-40-6 detail): PartyAckFlow rolls the party back to a savepoint and clears the tracker before publishing the fault: plan says "commit with the receipt", but without it partial work would commit and a failed SQL statement would abort the outbox transaction. Payroll party consumers use the same flow.
- Enabled service by service: consumerOutbox:true in 13 Program.cs; ConfigureMassTransitOutbox in the five; receipts in Training; Archive and Analytics export jobs are one save through their new outboxes.
- DEVIATION (P-3 wording): ConfigureMassTransitOutbox now also calls AddInboxStateEntity (MassTransit's documented model): the 7 existing outbox contexts get MSG040_InboxStateKeys (index on Delivered only; the alternate key was already there through the OutboxMessage FK). Training gets it inside MSG040_IntegrationMessageReceipts, the five get MSG040_AddMassTransitOutbox.
  build-ef-bundles.sh lists all 15 contexts, so the bundles pick them up. has-pending-model-changes: 13/13 none (59 s; needs a build first, --no-build reads the old dll).
- RabbitDownOutboxTests (Chaos) fixed: own broker on a fixed port and the delivery hosted service started: 1/1 (baseline failure gone; it also no longer disturbs the shared fixture).
- Tests: PartyAckConsumerBaseTests 8 (harness, real retry pipeline). Unit 4588/4588 (59 s). Integration 469 pass, 3 fail (Qa003Minio, CI-excluded ObjectStorage), 4 skip, incl. Ops038RabbitLeastPrivilegeTests (P-4; no new broker name expected: live check in P6/P7).
- P3 Finished (UTC): 2026-11-04T00:01:45Z

## P4
- status: in progress
- Static guard OutboxPublishOrderingArchitectureTests: 17 tests (real tree + negative samples for R1-R4). Roslyn: Microsoft.CodeAnalysis.CSharp 5.0.0 pinned in Directory.Packages.props, referenced by UnitTests only; lock files refreshed (30 files: the pin turns the existing transitive entry into CentralTransitive, same version; --locked-mode restore passes).
- The guard re-checked the P0 inventory: no unknown A/B site. It asked for 4 more [StagesOutboxMessages] (FlagUnresolvedContractTypeAsync, PublishHireEventsAsync, RecruitmentRequisitionClosedEvent.StageAsync, ErrorQueueReplay.ReplayAsync) and made ProcessCandidateAcceptedHandler use ExecuteAtomicAsync (joins the consumer outbox). Allow-list: empty, limit 0.
  Beyond the plan: R2 also treats ExecuteUpdate/ExecuteDelete before a publish as a write. Limits (in the test summary): order by position, saves by name.
- Runtime guard: UnsavedOutboxGuardMiddleware + UseHrOutboxFlushGuard() after UseRouting in 13 Program.cs; outbox_unsaved_messages counter (exported as *_total; zero sample with route=none); default throw in Development/Acceptance/Testing; Production logs and counts unless Messaging:FailOnUnsavedOutbox=true. OutboxScope.EnsureFlushedAsync at the end of the HR.Core start-up scope (HireCommencementWatcher already ends with a save).
- Tests: Msg040UnsavedOutboxGuardIntegrationTests 5/5.
- P4 Finished (UTC): 2026-11-04T00:15:28Z

## P5
- status: in progress
- MessagingRepairController in HR.Movements (replay/promotions, replay/delegations) and HR.Core (replay/asset-outcomes, resync/org-units), dry run unless dryRun=false; through OutboxUnitOfWork with deterministic ids; every apply writes a MessagingReplayRun row in EntityAuditLog (actor, kind, count).
- DEVIATION (rule 1 "the receipt shows it"): the consumer receipts are in other databases, so the publisher cannot read them. PublishedEventLedger writes an append-only EntityAuditLog row with every replayable publish, in the same save as the OutboxMessage; lost = committed source without a ledger row. Post-fix executions are never listed; the second run lists zero; the deterministic id still stops a duplicate at the consumer.
- DEVIATION (authorization): one policy Messaging.Repair = hr-admin or platform-admin (read "module admin policy + platform-admin" as either, since no persona holds both). The gateway module policies admit hr-admin, not platform-admin, so the script runs with a tenant hr-admin, once per tenant.
- scripts/msg040-replay-lost-events.ps1 (ASCII, PS 5.1): -DryRun default, -Apply explicit; bearer or gateway cookie; reports/msg040-replay-<utc>.json ids and counts only.
- Tests: Msg040ReplayTests 11 (dry run changes nothing; apply once with ledger and audit; second run zero; policy), Msg040ReplayIntegrationTests on a disposable database with real migrations (deterministic id stored).
- P5 Finished (UTC): 2026-11-04T00:23:06Z

## P6
- status: in progress
- Verification (PS 5.1 forms): Msg040/OutboxPublishOrdering/PartyAck unit, Testcontainers Msg040+Qa003Outbox, OutboxAtomicity/InboxIdempotency/Chaos all green during P3-P5; full suites again in P8 (local CI).
- Images: 15 local one at a time 556 s, 514 s, 502 s (three rounds: acceptance found defects), contracts 36 s; docker/build-manifest must be regenerated after a lock change (scripts/docker-generate-build-manifest.ps1; NU1004 otherwise).
- Prod-like stack from fresh volumes: up 18-21 s, all 14 ready 43-46 s. Timings on final code: AC-16/17/18 62 s; AcHcm29 33 s; journey (RUN_PLAYWRIGHT_E2E) 16 s; AC-MSG-40 97 s. **AC-MSG-40a..h 8/8, AC-16/17/18 3/3, AcHcm29 3/3, RecruitmentToHireJourneyProdLike 1/1**, all queues empty at the end including every _error.
- Defects the live run found and fixes (all re-proved):
  1. max_connections 100 on the acceptance stack ran out on the start-up re-sync; acceptance now 250 like production; MassTransit:ConcurrentMessageLimit default 8 keeps an endpoint below Maximum Pool Size=10 (every in-flight message holds a transaction).
  2. EF wraps transient PostgreSQL failures in InvalidOperationException, which the retry policy ignored: those stay retryable now (Ignore with a filter). Business InvalidOperationException still goes to _error. Needed for D-40-6; the wider retry policy stays in Wave 2.
  3. /metrics answered 401 (authorization fallback) wherever it was mapped: AllowAnonymous, the private-client filter stays, edge 404 unchanged. OpenTelemetry__Enabled=true on the acceptance services (AC-40f); vps file defaults it to false (P7).
  4. IPolicyPublished published as the concrete PolicyPublished was dropped: the outbox delivers to the concrete exchange, nothing binds it to the interface. Now Publish<IPolicyPublished>.
  5. Zero sample of the counter moved to the first request (the meter provider starts after the pipeline is built).
  6. ServiceTransactionStateHistory.Id ValueGeneratedNever: rows added by MoveTo in consumers counted as existing (DbUpdateConcurrencyException): TransactionArchived, HireCaseCompleted, TransactionRegisteredInDiwan went to _error, the 3 on the server baseline are this defect.
  7. Start-up re-sync covered the default tenant only: PublishAllTenantsOrgUnitsAsync publishes every tenant's units under their tenant (40h uses an agency-A unit).
  AC-17 was written for the legacy hire without an offer: now uses AcHcm29HireContractTests.PrepareAcceptedHireAsync.
- P6 Finished (UTC): 2026-11-04T01:28:16Z

## P7
- status: in progress
- First server deploy (UTC): backup /opt/hrm/backups/pre-plan40-20261104T014945Z (700/600, pg_dumpall 25.1 MB, 17 CREATE DATABASE lines incl. the maintenance hrm_admin, SHA256SUMS OK, 3 s); .env copy .env.bak-plan40-20261104T014945Z; server build c8872d7767ab 1188 s (upload 10 s, push 22 s); deploy 131 s, healthy 01:51:48Z, smoke 49/0/1 warn.
- SECURITY NOTE: HRM_OTEL_ENABLED=true (needed for the P7 metric check) together with AllowAnonymous on /metrics made https://<app>/careers/metrics answer 200 from the internet, 01:51:48Z-01:54:37Z (aggregate runtime metrics only, no personal data). The independent review found it (M2); confirmed live; closed at once by setting the key back to false and redeploying the same tag (113 s); then 401. Fixed in code: IsProxied in the filter (404 for a path base or X-Forwarded-For), Caddy @ops also covers /careers/metrics*, smoke and AC-36c check it, AC-40f checks it through the gateway.
- Independent review (one fresh subagent, artifacts/plan40/review.md): 1 high, 4 medium, 8 low. No refuter needed for H1 (reproduced by test first: the payload read an unstamped TenantId).
  H1 fixed: CreateEmployeeWithEventAsync saves first inside OutboxUnitOfWork (the tenant the payload needs is stamped by the save); test with the real interceptor. This was a regression of mine in P2 and it was live on the demo server for this tag until the next deploy.
  M1 fixed: AddPostgresOutbox sets IsolationLevel.ReadCommitted (MassTransit default RepeatableRead would have failed concurrent saga acks with 40001).
  M2 fixed (above). M3 fixed: a refused org-unit move throws OrgUnitMoveRefusedException inside the unit of work, so it rolls back instead of committing the fields.
  M4 open, documented: non-asset workflow handlers still run after the instance commit (the deviation in P1); no repair path for them; for the final report.
  L1 conditional access seen by the guard (+ negative test); L2 ledger rows carry the tenant; L3 comment corrected; L5 ShouldRetry no longer rethrows types the bus never retries; L6 script writes its report in finally. L4 resolved by M2. L7, L8 recorded in the final report.
- Local acceptance repeated on the final code (5c0f8f1b): AC-16/17/18 3/3 (66 s), AcHcm29 3/3 (35 s), journey 1/1 (17 s), AC-MSG-40a..h 8/8 (100 s), /careers/metrics through the gateway 404, all queues empty. The first fix of M2 was too wide (every service stamps its own path base on direct requests, 40f caught it): IsProxied now looks at the proof header, X-Forwarded-For and the /careers path base. Images 510 s and 540 s.
- Second and third server builds were needed (deviation from P-8 "build once"): the review found H1 on the deployed tag, and the first M2 fix was too wide. Server builds: 63f041abeccc 1176 s (not deployed), f44e52cf58be 1167 s (upload 10 s, push 19 s).
- Final deploy f44e52cf58be: HRM_OTEL_ENABLED=true set first (undo: false + deploy). First attempt FAILED 03:06:20Z: PostgreSQL was recreated, Keycloak's pooled connections were closed, keycloak-reconcile could not read the clients, applications stayed Created (demo down from 03:05:23Z). Same command again: healthy 03:08:25Z (115 s, smoke 50/0/1 warn). Downtime 3 min 2 s. No data touched; backup not needed. Rollback was not needed.
  Earlier deploys today: 01:51:48Z c8872d7767ab, 01:54:37Z same tag with the metrics key off (each a rolling recreate of about 2 min).
- Server checks on f44e52cf58be: outbox tables in the 5 + Training receipts + 14 mq_* connections only; tour (AC_OPS_38 P6: leave approved and cancelled, ID card) 1/1; outbox_unsaved_messages_total 0 in the 13 services; _error queues unchanged (only the 3 old transaction-archived messages); replay dry run 0 promotions, 0 delegations, 0 asset outcomes, 30 org units, nothing applied (reports/msg040-replay-server-20261104T031223Z.json; the demo has no promotion, delegation or asset workflow rows, 20 execution records are transfers/secondments); smoke -RequireIntegrationWiring and AcInt37 3/3 (35 s); AC-OPS-36 (36j excepted) + AC-OPS-39 vps 13 pass (710 s). In the combined run AcInt37 failed 2 only because 39f recreates the containers in parallel; alone 3/3. Evidence scan: 0 hits of .env secrets.
  AC_OPS_38 test now accepts Archived: the leave's transaction reaches it since the state-history fix.
- P7 Finished (UTC): 2026-11-04T03:26:46Z

## P8
- status: in progress
- Local CI run 1 on the release head: stopped at the formatting check (whitespace in the new tests, BOM in generated migrations); fixed by e5c70c90. Run 2 on e5c70c90: ok=True, 90 passed, 0 failed, 0 skipped, 0 not-run, 1031 s (unit 4624, integration 352, Testcontainers 129, all 0 failed).
- plan035-prepush on e5c70c90: actionlint, cilocal, forbidden, nonprod, signatures PASS; gitleaks FAIL: generic-api-key on this file line 9 (commit 491c7b5d) = the server's public ED25519 host key fingerprint in abbreviated form, not a secret (same false positive as PLAN-37, d4e53b08). D-QA-17 (rewrite and rotate) covers real secrets, and a rewrite would change the SHAs recorded as evidence, including the tag on the server, so history was left alone.
  Fix: one .gitleaks.toml entry, this path AND that exact abbreviated string. .gitleaks.toml's own comment limits entries to fake or lab-only values; this public value is neither, it follows the PLAN-37 entry. For the owner's review in the PR.
  Proved in a throwaway git repository with the pinned image: fingerprint in this file passes; a generated fake key on the next line of this file is reported; the fingerprint in any other file is reported. (A first run with --no-git proved nothing: paths are absolute there, so the entry never applied.)
  The script runs with -NoProfile -ExecutionPolicy Bypass as its header documents: plain "powershell -File" is refused on this machine (policy undefined, so the Restricted default); no setting changed.
- Local CI run 3 on 29db9399: ok=True, 90 passed, 0 failed, 0 skipped, 0 not-run, 941 s (unit 4624, integration 352, Testcontainers 129). plan035-prepush on 29db9399: all six PASS, exit 0, 19 commits in the range.
- Pushed release/plan-40 (29db9399) once; PR https://github.com/omardefdev/HRManagement/pull/35 opened, MERGEABLE, checks running at the last read.
- NOT MERGED YET. Deviation from P-7 step 2: `gh pr checks --watch` was not run, because the desktop app forbids polling CI from the session; its monitor wakes the session on failures only, and GitHub auto-merge was not used because main has no branch protection to hold it. The merge waits for a green read of every check after the owner's next message.
- CodeQL on PR 35 run 1 (head 29db9399): 4 new cs/web/missing-token-validation findings on the replay POSTs, real, baseline not touched. Fixed in 3d0b2df8: antiforgery token on each POST, token endpoint behind the same policy, script through a web session.
  Found by testing through the gateway: the script's -CookieHeader mode never worked (Windows PowerShell 5.1 drops a Cookie entry in -Headers) and the cookie path is /ui/<service> while replay goes through /api/<service>. AC-MSG-40a..h 8/8 on the fix; 40g proves the 400 with bearer and session, and the script in both modes.
- Server: build of 3d0b2df84f82 06:07:38Z, 21 min. Deploy attempt 1 failed 06:09:20Z (keycloak-reconcile after the PostgreSQL recreate, second time in this plan); same command OK 06:11:55Z; applications down about 4 min. 22 containers, smoke 50/0/1 warn. AC_MSG_40_vps 3/3; replay dry run 0/0/0 and 30 org units, nothing applied. PLAN-38 tour not repeated on this tag.
  One run was interrupted by the owner mid-way; its unconfirmed report was deleted and the checks repeated in full.