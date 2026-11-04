# Task: PLAN-40 — Domain events are published atomically across the services

The deliverables:
- **Every publish** is written to the outbox in the same transaction that commits the state.
- **Consumers and sagas** publish through the consumer outbox.
- **Two guards** stop regressions: a static architecture test, and a runtime check for unsaved outbox rows.
- **Events lost before the fix** are listed by a dry-run replay tool.
- **The demo server** is updated.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order.

**There are no owner actions in this plan.** Every decision is taken (see «Decisions»).

**Standing owner instruction on merges** (2026-11-02):
- for PLAN-38: «إن لم يكن مدموجاً اجعله يدمجه هو»;
- for PLAN-39: «نفذها انت».

So **you** push the release branch, open the PR, watch the checks, and merge — but **only when every check is green** (P-7).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan40-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan40-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Check the server's state before any server step.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan40-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventory, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session **without ultracode**, at `xhigh`):
- Use **at most two subagents at a time**. Good uses:
  - the publisher inventory in P0, one per service group, read-only;
  - root-causing separate failures.
- Run **one independent review** at the end. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, test runs) to files under `artifacts/plan40/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET messaging engineer:
- MassTransit 8 (bus outbox, consumer outbox `UseEntityFrameworkOutbox`, inbox state, sagas, retry, scheduling);
- EF Core transactions and execution strategies on PostgreSQL;
- Roslyn syntax analysis for architecture tests;
- PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..39 did.

**Where things stand:**
- **Services:** 15 services, 8 with a bus outbox. No consumer outbox anywhere.
- **Publishing:** several places publish after their last save, which loses the event (class A). Others save, publish, then save again without one transaction (class B). Consumers publish party acknowledgements straight from `ConsumeContext` inside the inbox transaction (class C).
- **The demo server:** runs `817304f5bd88`. Bootstrap migrations run as `*_migrator`, the broker uses `mq_*` users, Keycloak has its own role, and the key rings are guarded.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-40-msg-atomic-event-publishing.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. `reports/plan39-final-report.md`:
   - §3 deviations;
   - §6 the server (tag, backup, changes and undo);
   - §8 defects (the plain acceptance stack's root-owned volumes, and the evidence summarizer under PS 5.1);
   - §10 readiness: start-up checks go through `AddHrStartupCheck<T>()`, and the realm is code.
3. `reports/plan38-final-report.md` §4 (the RabbitMQ inventory and the `mq_*` permission expressions) and §12. Consumer outbox must not need new broker names. If it does, the matrix in `deploy/bootstrap/rabbitmq-users.json` is updated in the same commit.
4. `docs/OPS_VPS_SINGLE_HOST.md`: build-on-host, deploy, rollback, smoke, the backup and restore section, and the server change table.
5. `docs/OWNER_DECISIONS_REGISTER.md`: D-40-1..6 are accepted.
6. `docs/EVENT_PUBLISHER_INVENTORY.md`, `docs/RABBITMQ_OPERATIONS.md`, and every file the plan cites (`MassTransitExtensions.cs`, `IdempotentConsumerFilter.cs`, `PartyAckConsumerBase.cs`, the class A sites, the `Program.cs` and `*DbContext.cs` files of the five services without an outbox, and `TrainingDbContext.cs`).

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **PLAN-39 is merged with green checks — merge it yourself if it is not** (standing owner instruction). Its release branch `release/plan-39` is pushed. Check with `gh pr list --state all --head release/plan-39`.
   - **Merged:** continue.
   - **Open:** run `gh pr checks <n> --watch --fail-fast`; only if it exits 0, run `gh pr merge <n> --merge`.
   - **No PR yet:**
     1. Push the release branch if the remote is behind.
     2. `gh pr create --base main --head release/plan-39 --title "PLAN-39: Keycloak realm as code and persistent Data Protection keys" --body-file reports/plan39-pr.md`
     3. Watch and merge as above.
   - **A red check:** root-cause it (`gh run view <id> --log-failed`), fix it on `release/plan-39` with a small commit naming the step, rerun that step locally, push the release branch only, and watch again. If the fix needs more than a small change, record `BLOCKED:`.
   - **Never merge with a red or pending check.**
   - **Record** the PR link, check results, the merge commit and whether the post-merge dedupe guard skipped, in `reports/plan39-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`.
2. **`main` is in sync.** `git switch main; git pull --ff-only`; then `git rev-parse main` = `git rev-parse origin/main`.
3. **The working tree is clean** except for these, which may be present:
   - the owner's local `global.json` edit;
   - the untracked `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - this prompt file;
   - the uncommitted `reports/plan39-progress.md` and `reports/plan39-pr.md` notes, which ride with this plan's first commit.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`.
4. **No other plan session** is running on this working tree. The PLAN-39 session may still be finishing its merge.
5. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.

## Decisions — already taken (no owner questions)

**D-40-1..6**, accepted by the owner on 2026-10-31, as the plan's table states:
- **D-40-1:** the MassTransit consumer outbox (`UseEntityFrameworkOutbox<TDbContext>`) on every receive endpoint of services that publish from a consumer or saga.
- **D-40-2:** change, publish, then one save. `OutboxUnitOfWork` is used only where a database-generated value is needed (a sequence, or `ExecuteUpdate`).
- **D-40-3:** `MessageId = UUIDv5(namespace, "<EventType>:<AggregateId>:v1")` for repaired and replayed events.
- **D-40-4:** replay scope covers promotions, delegations, asset outcomes and org units. `PolicyPublished` is not replayed, and `IAgencyOnboarded` has no consumer. **The replay is a dry run by default.**
  - The tool is built and dry-run on the acceptance database and on the demo server.
  - **Applying it is not part of this plan.** The command is documented for the owner.
- **D-40-5:** the runtime guard throws in Development, Acceptance and tests (`Messaging:FailOnUnsavedOutbox=true`). In Production it logs an error and increments a metric.
- **D-40-6:** party acknowledgements:
  - a business failure publishes `*PartyFaulted` once and commits without rethrowing;
  - a transient failure rethrows without publishing while retries remain, and on the last attempt publishes once and returns.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Prove the filter order before enabling it broadly.** Plan §4 rule 3 says it first. Write `Msg040OutboxOrderingIntegrationTests` (Testcontainers PostgreSQL + RabbitMQ) **before** turning on `consumerOutbox: true` in any service. The test must show that the inbox join mode and the consumer outbox share one transaction: no `already an active transaction`, a duplicate gives one effect, and a failed commit leaves nothing on the broker. Then enable the outbox service by service, running each service's tests after each change. A transaction conflict that the join mode cannot resolve is a **Stop**. |
| P-2 | **Roslyn in tests only.** Add `Microsoft.CodeAnalysis.CSharp` as a **test-only** package, pinned in `Directory.Packages.props`, with the lock files refreshed (`dotnet restore --force-evaluate`) in the same commit. It is never referenced by production projects. If it cannot be added under `--locked-mode`, use the plan's text fallback, and record the reason. |
| P-3 | **New migrations go through the bootstrap.** The `MSG040_*` migrations (five outboxes, plus Training receipts) are applied on the server by `hrm-bootstrap` as `*_migrator` (PLAN-38). Check that `build-ef-bundles.sh` picks them up. Run `dotnet ef migrations has-pending-model-changes` for every changed context with the repository's tool manifest (`.config/dotnet-tools.json`, `dotnet tool restore`). |
| P-4 | **Broker permissions.** The consumer outbox delivers through the same bus, so no new exchange or queue names are expected. After enabling it, rerun the PLAN-38 least-privilege broker test (`Ops038RabbitLeastPrivilegeTests`) and the live `mq_*` checks. If a new name appears, extend `rabbitmq-users.json` from the live inventory in the same commit; never widen a pattern beyond the service's own names. |
| P-5 | **The plain acceptance stack must start on its own** (PLAN-39 defect). `docker-compose.acceptance.yml` gets the same `acceptance-volume-init` one-shot as `docker-compose.acceptance-leastpriv.yml`: `chown` of the acceptance Data Protection and file-storage volumes to 1654 before the applications. Tell PLAN-41 in the readiness note that it is done. If the evidence summarizer (`scripts/acceptance-evidence-summarize.ps1`) still writes a BOM or drops the appended scenario log under PS 5.1, fix it: write UTF-8 without a BOM, and keep the log. Both are small; anything bigger stays with PLAN-41. |
| P-6 | **PowerShell 5.1.** Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block. Start the prod-like stack with the plan's `docker compose … up -d --wait` command directly. Build local images one at a time. |
| P-7 | **Merge, by you, only after green checks** (standing owner instruction).<br>1. Push **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`, then `gh pr checks <n> --watch --fail-fast`.<br>3. Only if that exits 0: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and watch again.<br>5. Record whether the post-merge dedupe guard skipped the heavy jobs.<br>Never push to `main` directly, never force-push. |
| P-8 | **The demo server.**<br>• Build once, on the final code of P6 (`build-on-host.ps1`, then `deploy.ps1 -Tag <sha12>`).<br>• Back up first with `pg_dumpall` under `/opt/hrm/backups/pre-plan40-<utc>/` (700/600), verified as before (all 16 `CREATE DATABASE` statements, `SHA256SUMS`). This deploy adds tables through migrations.<br>• The demo stays usable at the end of every turn. If a step fails and cannot be fixed in the turn: `deploy.ps1 -Rollback` to `817304f5bd88`, which is safe because the old code ignores the new tables. Restore the backup only if data was damaged, prove the demo works, and record the downtime. |
| P-9 | **No regression of PLAN-36..39.** These stay green:<br>• `lint-compose.ps1` (both chains), `Ops036ComposeLintTests`;<br>• `ProductionWiringMatrixTests` and `int037-wiring-check.ps1`;<br>• `Ops038EnvParityTests`, `Ops038RabbitPermissionMatrixTests`;<br>• `Ops039RenderRealmTests` and `Ops039RealmManifestParityTests`;<br>• the start-up checks (`AddHrStartupCheck<T>()`).<br>**The contract for later plans** (plan §5 rule 3): any plan that adds an event or a publisher adds its row to `EVENT_PUBLISHER_INVENTORY.md` in the same commit (R4), and publishes with the patterns here. Write it into the inventory document and the readiness note. |

## Scope boundaries

**Out of scope** (from the plan):
- **Wave 2 (rest of G-MOV-05):** the semantics of promotion and delegation (the grade in HR.Core, the Payroll consumer, eligibility).
- **PLAN-45:** the custody approval loop.
- **PLAN-46:** binding other services to the engine.
- **PLAN-47 §8:** the single policy-publishing service, the extended `IPolicyPublished` payload, and cache invalidation.
- **PLAN-62/63:** executing the separation and hire parties.
- **Wave 2 (OPS-F, OPS-H):** the retry policy, second-level redelivery, the `_error` replay tool and its alerting, and cross-service reconciliation.
- **Wave 2 (OPS-D):** RLS.
- **Wave 2:** message ordering in the Training consumers.
- **D-40-4:** applying the replay on the server.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- test-rewritten `testdata/*/out/*.json`;
- any secret, token or password;
- replay reports with personal data (the tool writes ids and counts only).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- disable host-key checking;
- apply the replay anywhere but a disposable test database;
- grow `outbox-publish-allowlist.txt` without a written reason;
- leave the demo server down at the end of a turn;
- weaken a guard, lower a coverage floor, or delete a CI step to get green.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** — including a PLAN-39 check that stays red after a small fix.
- **The inbox join mode and the consumer outbox conflict** in the P-1 integration test in a way the plan's design cannot resolve.
- **A class B site needs a database-generated value** that `OutboxUnitOfWork` cannot cover without changing the domain model (for example an id produced by a trigger).
- **The consumer outbox needs a broker permission** that would let a service touch another service's names (P-4).
- **A server deploy fails, and neither `-Rollback` nor the restore brings the demo back.**

---

## P0 — Timing, preconditions, carry-over, baseline

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions; the PLAN-39 merge comes first.
2. `git switch -c feature/plan-40-msg-atomic-event-publishing` from the updated `main`.
   - Commit this prompt file.
   - Commit the PLAN-39 notes: `docs(ops039): PR merge note and post-merge run`.
3. **The P-5 carry-over:** add `acceptance-volume-init` to the plain acceptance file, and fix the summarizer if needed. Prove it: start a **fresh** plain acceptance stack, and HR.Core and File.Storage come up healthy with no manual step.
   - Commit: `fix(msg040): plain acceptance stack owns its volumes before first boot; evidence summary keeps its log and writes UTF-8 without BOM`.
4. **Plan baseline** («المرحلة 0» steps 0.2–0.5) into `reports/plan40-baseline.md`:
   - **0.2:** build, and the listed filters.
   - **0.3:** `reports/plan40-publisher-inventory.md`, from a temporary Roslyn scan that becomes the static guard in P4. Classify every site as **A**, **B**, **C** or **OK**, with file and line.
   - **0.4:** live confirmation of class A on the prod-like stack: count `OutboxMessage` rows before and after a promotion, and check the receipt in HR.Core. The expected result is no row and no receipt.
   - **0.5:** the comparison with `EVENT_PUBLISHER_INVENTORY.md`, listing the events missing from it.
5. **The server's current state, read-only:**
   - per service, whether outbox tables exist;
   - the `mq_*` connections;
   - the running tag;
   - the queue depths, including the `_error` queues.

Commit: `chore(msg040): baseline, publisher inventory (A/B/C), live class-A confirmation, server before-picture`.

## P1 — The atomic unit of work and class A (plan §1, §2)

- `OutboxUnitOfWork` (joins an ambient transaction, and uses the execution strategy), `[StagesOutboxMessages]`, `DeterministicMessageId` (UUIDv5).
- Fix every class A site in the plan's table, plus any the inventory found:
  - `IPromotionExecuted` and `IDelegationExecuted`: publish before the single save, with deterministic ids;
  - `ApproveAsync`/`RejectAsync` wrapped in `OutboxUnitOfWork`;
  - `PublishAllOrgUnitsAsync` saves;
  - the two org-unit stagers marked;
  - `IAgencyOnboarded` published before its save;
  - `IPolicyPublished` moved into `PolicyVersionPublisher.PublishDraftAsync` before its save, and removed from the controller.
- **Tests:**
  - `DeterministicMessageIdTests`;
  - unit tests per site: the outbox row is in the same `SaveChanges`;
  - the PLAN-25 gate and handler tests as mandatory regression for `ApproveAsync`.

Commit: `fix(msg040): class A publishers write their outbox row in the same save (promotions, delegations, asset outcomes, org units, agency onboarding, policy publishing), atomic unit of work and deterministic message ids`.

## P2 — Class B (plan §3)

- **Every class B site** from the inventory becomes «change → publish → one save», or `OutboxUnitOfWork` where a database value is needed.
  - `EmployeeIntegrationService.CreateEmployeeWithEventAsync` becomes one save.
  - `PublishStatusChangedIfNeededAsync` is marked `[StagesOutboxMessages]` and stops saving; its callers save once.
- **`EVENT_PUBLISHER_INVENTORY.md`:**
  - remove «Save → Publish → Save» as the required pattern;
  - give every row a pattern column (`SingleSave` or `OutboxUnitOfWork`);
  - add the events that were missing.
- **Tests:** a failure-injection integration test on the last save (state and message both absent), and each changed service's unit and integration tests.

Commit: `fix(msg040): class B save-publish-save sequences become one save or one transaction, publisher inventory corrected`.

## P3 — Class C: the consumer outbox (plan §4, P-1, P-4)

1. **First:** `Msg040OutboxOrderingIntegrationTests` with the join-mode `InboxConsumeFilter` (P-1).
2. **Then:**
   - `consumerOutbox: true` in `AddPostgresOutbox<TDbContext>`, with the endpoint callback;
   - an outbox for AdminLogistics, AuthDirectory, ITInfrastructure, RecordsArchive and DevAnalytics: `ConfigureMassTransitOutbox()` and `MSG040_AddMassTransitOutbox` migrations;
   - Training receipts (`MSG040_IntegrationMessageReceipts`);
   - `PartyAckConsumerBase` with `MessagingFaults.IsTransient` and the D-40-6 behaviour;
   - the export-job publishers of Records.Archive and Dev.Analytics through their new outboxes.
3. Enable the outbox service by service, running each service's tests after each change.
4. Run `dotnet ef migrations has-pending-model-changes` for every changed context (P-3).
5. Rerun the broker least-privilege test (P-4).
6. **Tests:**
   - `PartyAckConsumerBaseTests` (business, transient with retries left, transient on the last attempt);
   - the integration cases from the plan's Deliverables.

Commit: `feat(msg040): consumer outbox on every publishing service (join-mode inbox, no message before commit), outboxes for the five services without one, Training receipts, party acknowledgements publish once`.

## P4 — The two guards (plan §5)

- **`OutboxPublishOrderingArchitectureTests`** (rules R1–R4, P-2), with a test of the guard itself on broken samples, and `tests/architecture/outbox-publish-allowlist.txt`: empty, or every line justified.
- **`UnsavedOutboxGuardMiddleware`** with `UseHrOutboxFlushGuard()` in the 13 outbox services, the `outbox_unsaved_messages_total{service,route}` metric, and D-40-5 behaviour.
- **`OutboxScope.EnsureFlushedAsync`** at the end of the start-up and background scopes that publish.
- **Tests:** an integration test with a controller that publishes without saving, which throws in test mode.

Commit: `feat(msg040): static outbox-ordering guard (R1-R4) and runtime unsaved-outbox guard in the 13 outbox services`.

## P5 — Replay of lost events (plan §6, D-40-4)

- `MessagingRepairController` in HR.Movements (`replay/promotions|delegations`) and in HR.Core (`replay/asset-outcomes`, `resync/org-units`), with the plan's authorization.
- Replays go through `OutboxUnitOfWork` with deterministic ids, and every non-dry run is audited in `EntityAuditLog`.
- `scripts/msg040-replay-lost-events.ps1` (PS 5.1): `-DryRun` is the default, and `-Apply` is explicit. It writes `reports/msg040-replay-<utc>.json` with ids and counts only.
- **Tests:** a dry run lists the injected «before the fix» rows; an apply on a **disposable** test database produces the effect; a second run lists zero.

Commit: `feat(msg040): dry-run-first replay of events lost before the fix (promotions, delegations, asset outcomes, org-unit resync), audited`.

## P6 — Acceptance on the prod-like stack (plan Acceptance, Verification)

1. Run every unit, Testcontainers, SQLite and Chaos filter in the plan's Verification block (PS 5.1 forms).
2. Start the prod-like stack with the plan's command (P-6). Record the time to all healthy.
3. **`AcMsg40AtomicPublishingTests`** (`[RequiredAcceptance]`, `AcceptanceGate`, `EvidenceRecorder("AC-MSG-40", …)`): AC-MSG-40a..h, positive and negative.
   - Also rerun **AC-16, AC-17 and AC-18 live**. This replaces the failing AC-16 evidence (`"ok": false`, `"receiptCount": -1`).
   - 40g injects «before the fix» rows with SQL and runs the replay **dry run, then apply, on this acceptance database only**. The second run must list zero.
4. **Regression:** `AcHcm29`, and the prod-like recruitment journey E2E (`RecruitmentToHireJourneyE2ETests` with its prod-like category).
5. Run with `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`; all must Pass.
6. Commit `acceptance-evidence/AC-MSG-40/` and the AC-16/17/18 folders, plus the regenerated summary.

Commit: `test(msg040): AC-MSG-40a..h and live AC-16/17/18 pass on the prod-like stack, AcHcm29 and the prod-like journey still green, evidence`.

## P7 — The demo server (P-8)

Record every server change and its undo command **as you make it**.

1. Run `build-on-host.ps1` once, on the final P6 code.
2. Take the backup, then **verify** it.
3. Deploy with `deploy.ps1 -Tag <sha12>`. `hrm-bootstrap` applies the `MSG040_*` migrations as `*_migrator`. Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `acceptance-evidence/AC-MSG-40/vps/`:
   1. The outbox tables exist in the five services and the Training receipts table exists.
   2. `rabbitmqctl list_connections user` shows only `mq_*`.
   3. After a short UI tour with the demo users (a leave approval, an asset assignment or a promotion where a demo user can reach it), `outbox_unsaved_messages_total` is 0 in every service (`/metrics` inside the network), and the `_error` queues did not grow.
   4. **The replay dry run on the server**, through the script over an SSH tunnel or a server-side call: record the counts in `reports/msg040-replay-server-<utc>.json` (ids and counts only). **Do not apply (D-40-4).** Write the owner's apply command into `OPS_VPS_SINGLE_HOST.md`.
   5. Smoke `-RequireIntegrationWiring` is green.
   6. `AcOps36VpsBootTests` (36j excepted), `AcInt37VpsServerTests`, the PLAN-38 server P6 scenario (leave and ID card) and the PLAN-39 server checks (Keycloak role, realm settings, canary) all pass.
5. **If anything fails and cannot be fixed in the turn:** roll back (P-8), prove the demo works, and record it.

Commit: `test(msg040): demo server on atomic publishing (new outboxes migrated by the bootstrap, no unsaved outbox rows after a tour, replay dry run recorded), PLAN-36..39 server checks still green`.

## P8 — Docs, review, final report, merge through a PR (by you)

1. **Docs:**
   - `EVENT_PUBLISHER_INVENTORY.md`, with the P-9 contract;
   - `RABBITMQ_OPERATIONS.md` (the Outbox section);
   - `OPS_VPS_SINGLE_HOST.md`: the replay command for the owner, the backup location, and the change table;
   - the «الحالة» lines for G-OPS-08 (the covered part), G-WF-03, and the publishing part of G-MOV-05;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Independent review** (one fresh subagent). It reads this prompt, the plan and the diff, and hunts for:
   - a publish that can still leave without its state, or state without its publish;
   - a consumer message that can leave before its commit;
   - a duplicate that produces a second effect;
   - a guard that cannot fail (its own negative test must fail on a broken sample);
   - an allow-list line without a reason;
   - a replay that can run without a dry run or without an audit;
   - personal data in a replay report;
   - a broker permission wider than needed.

   Use a refuter only for high findings. Fix what survives, with tests.
3. **Local CI, then the pre-push check, before any push:** `powershell -File scripts/ci-local.ps1` on HEAD, then `powershell -File scripts/plan035-prepush.ps1`. Both must be green.
4. **`reports/plan40-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. The publisher inventory before and after (counts of A, B and C, and every site fixed).
   5. The guards, and their own negative tests.
   6. Unit, integration and acceptance results.
   7. The server: before and after, the replay dry-run counts, server changes and their undo, the backup location.
   8. Deferred items.
   9. Defects for later plans.
   10. **Timing:** total wall-clock; active time (there should be no owner wait); inventory, builds, acceptance and deploy times; Actions minutes and whether the post-merge guard skipped; and the output of `powershell -File scripts/plan-timing.ps1 -From 25 -To 40`.
   11. A readiness note for PLAN-41: the P-5 carry-over is done, and the P-9 contract applies.
5. **Merge (P-7), by you:**
   1. Merge the feature branch into a new `release/plan-40` from `main` (`--no-ff`).
   2. Write `reports/plan40-pr.md` (short).
   3. `git push -u origin release/plan-40`
   4. `gh pr create --base main --head release/plan-40 --title "PLAN-40: atomic event publishing" --body-file reports/plan40-pr.md`
   5. `gh pr checks <n> --watch --fail-fast`
   6. Only if step 5 exits 0: `gh pr merge <n> --merge`
   7. A red check: fix it on the release branch, push that branch only, and watch again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan40-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. That note rides with the next plan's first commit; **do not open a second PR for it.**

## Definition of done

- **The inventory:** no unfixed A, B or C site. `outbox-publish-allowlist.txt` is empty, or every line is justified.
- **The guards:** the static guard is green and fails on a broken sample. The runtime guard is on in 13 services.
- **The outboxes:** the consumer outbox is on in 13 services, and the migrations for the five new outboxes and Training are applied (locally and on the demo server).
- **The tests:** AC-MSG-40a..h and AC-16/17/18 = Pass, with evidence. `AcHcm29` and the prod-like journey are green.
- **The replay:** dry-run on the acceptance database (and applied there for 40g), dry-run on the demo server with its counts recorded. The owner's apply command is documented.
- **The documents:** `EVENT_PUBLISHER_INVENTORY.md` and `RABBITMQ_OPERATIONS.md` are corrected, and the status lines are updated.
- **The server:** the PLAN-36..39 server checks are green.
- **The merge:** done by you, through a PR, **after** green checks, with the timing section.

Then reply with a short summary in Arabic:
- what changed;
- the server's replay dry-run counts;
- the PR link;
- the path of the final report.
