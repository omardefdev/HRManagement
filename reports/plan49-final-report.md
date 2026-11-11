# PLAN-49 — final report: what the policy engine governs, facts that match their schemas, and no dead or orphan rules

Specification: `docs/acceptance-plans/execution/PLAN-49-AGENT-PROMPT.md` (it wins over the plan,
`docs/acceptance-plans/PLAN-49-pol-engine-scope-orphans.md`). Checkpoint with every decision and stamp:
`reports/plan49-progress.md`. Baseline and inventories: `reports/plan49-baseline.md`.

## 1. Summary per phase

| Phase | What it delivered |
|-------|-------------------|
| P0 | The ClamAV host port is gone (PLAN-48's defect: the acceptance stack could not start on the owner's machine). Baseline, the domain reality table, and eight inventories (modes and clients, bindings and readers, drafts and overrides, facts against schemas, the leave channels, the tests and scripts the change reaches, the acceptance classes, the server's picture). |
| P1 | `PolicyDomainCatalog` (SharedKernel): one descriptor for each of the twenty domains, five `Governed`, fifteen `OutOfScope` with a reason. The validator (`CatalogPolicyDomainCapabilities`), the engine, the internal endpoint, the start-up check, the policy page and `docs/POLICY_DOMAIN_REGISTRY.md` all read it; `PolicyDomainOwners` and the workflow allow-list are views of it; `PolicyDomainRegistry`, `AllowAllPolicyDomainCapabilities` and `PolicyDomainGateway` are deleted. The enforcement client is registered only by the three services that call it. |
| P2 | The migration `POL049_PolicyDomainRetirement` (two nullable columns, no data). The operation `RetireDomain` and four change sets: `POL-049-SCOPE-RETIRE` (fifteen final versions with no rules), `POL-049-ASSETS-ACTION`, `POL-049-IDCARD-ORPHAN`, `POL-049-LEAVE`. A new database seeds the five governed domains only, already conforming, with seven bindings; `ESTABLISHMENT-CHANGE-APPROVAL` gets the binding it lacked. |
| P3 | The facts builders send what the schemas hold. Leave asks the engine on create, update, approve and the self-service event, after the local validation; a self-service request that is refused is created `Rejected` with its reason (D-POL-14). The establishment submit sends the actor's real scope. |
| P4 | The five governed domains are `Shadow` in the Production and Staging files of HR.Core and of each owner, and on the prod-like stack. `PolicyWorkflowCodeGuard`: a policy instance of another workflow than the one the service asked for is cancelled in the caller's transaction and the start is refused (G-POL-09). Shadow ledger rows proved under forced row security as a non-owner role. The client times every Shadow call. |
| P5 | AC-POL-49a..g on the prod-like stack (two classes, one overlay stage with its own CI job), the boot check, the CI scope and ownership. One independent review (8 findings) and, on the owner's switch to the session's multi-agent mode, two adversarial verifications (12 findings, then 35 from two launches of the last one, many of them the same): no high finding survived as high, 7 were medium, and all were answered with code, a test or a recorded decision (section 3). |
| P6 | Nothing to merge: `origin/main` was still `f99950bf` (2026-11-11T00:40Z, and checked again before the push). No commit. |
| P7 | The demo server runs `046316bde9e7` (the P5 commit) since 2026-11-11T01:05:34Z: a verified backup, one detached build (17 of 17 images), a deploy that passed at the first attempt, the migration and the four change sets applied at the first start. Fifteen templates are retired, the five governed domains run in Shadow in four services, and each has ledger rows from its owner since the deploy. The three server checks of this plan pass, with the server checks of PLAN-36..48 (section 8). |
| P8 | The documents (the section of the runbook on the scope of the engine, with the rollback note; the status and the notes of the plan), this report, local CI and the pre-push check on the release HEAD, the PR and the merge. PR 47 was merged at 2026-11-11T09:13:33Z (merge commit `1ee60c47`) after its second run went 16 of 16 green; the first run had one red step, fixed on the release branch (section 6). Every check, the timing and the server check after the merge are in the merge note of `reports/plan49-progress.md`. |

## 2. Commits

| Commit | Phase | Message |
|--------|-------|---------|
| `ec1b2729` | P0 | docs(ten048): merge note for PR 46 (cherry-picked from `ea91a632`) |
| `626a0022` | P0 | docs(pol049): the PLAN-49 agent prompt |
| `ec9dca37` | P0 | fix(qa): the acceptance stack no longer binds ClamAV to host port 53310 (PLAN-48 §10) |
| `26c43704` | P0 | chore(pol049): baseline, the domain reality table, modes and clients everywhere, bindings and their readers, open drafts and overrides, the server's templates and ledger |
| `c4eade5b` | P1 | feat(pol049): one domain catalog drives the validator, the engine, the internal endpoint, the start-up check and the policy page; the client stays only where it is called |
| `4f6aa0dc` | P2 | feat(pol049): fifteen domains retired by a final version; asset rules bound to Assign; the orphan card rule and the placeholder leave rules retired; a fresh database seeds the five governed domains |
| `48ba1ef6` | P3 | feat(pol049): facts match their schemas; leave passes the engine on create, update, approve and ESS; establishment sends the actor's real scope |
| `f40c5f01` | P4 | feat(pol049): the five governed domains run in Shadow in Production, Staging and prod-like; a policy may start only the workflow the service asked for |
| `046316bd` | P5 | test(pol049): AC-POL-49a..g pass on the prod-like stack; Shadow ledger rows proved under forced row security; review fixes; CI scope and ownership updated |
| `2e14fbac` | P7 | test(pol049): demo server on the domain catalog (fifteen domains retired, five in Shadow with live ledger rows), PLAN-36..48 server checks still green |
| `710bf420` | P8 | docs(pol049): the engine scope in the server runbook with the rollback note; the plan status and notes; the final report and the PR body |
| `3005a21c` | P8, after the PR's first run | fix(qa): a finished test's idle connections leave the shared PostgreSQL server after seconds, not five minutes |
| `a58e7194` | P8, after the PR's first run | docs(pol049): the PR's first run, its one red step and the fix, in the reports and the plan's notes |

P6 has no commit. The release branch adds one merge commit (`17a9076f`, `release/plan-49`, `--no-ff`); the two last commits
are on the release branch only, as the prompt orders for a red check. The merge commit of PR 47 on `main` is `1ee60c47`.
The merge note (this text and the progress file) is one more commit on the local branch `docs/plan-49-merge-note`, never
pushed; the next plan brings it in.

## 3. Decisions applied and deviations

The owner's decisions applied as written: D-POL-1 (five governed domains in Shadow, fifteen out of scope), D-POL-5 (no
placeholder legal value in the engine: `R-LEAVE-MAX` and `R-LEAVE-LONG` retired, the local limits untouched), D-POL-13 (no
domain in Enforce in any production file), D-POL-14 (a refused self-service leave is created `Rejected`, never dropped),
D-POL-2 and D-POL-4 unchanged. The 72 decisions taken on the way, each with its reason, are in the progress file.

**Deviations from the prompt** (the prompt wins over the plan; these are the places where the work differs from the prompt):

| # | The prompt said | What was done, and why |
|---|-----------------|------------------------|
| 30 | `ExternalApprovalService` goes through the coordinator, so the new check sits there too (P-7) | It does not go through the coordinator. The engine starts no workflow for an external type (PLAN-43), so no policy instance can reach that service: a test proves it for a registered type, and a catalog test keeps every registered type out of the engine's own starts |
| 38 | `prodLike:pol49` in the `prodlike` job, after `prodLike:s2s42` (P-10) | It has its own job `prodlike-policy` and sits before `s2s42` in `order[]`: `Qa066WorkflowTests` forbids a later stage on a stack an overlay changed, the PII scan must keep reading the stack the prod-like classes used, and a second stack start would pass the job's limit |
| 7 | AC-POL-49e drives a branch manager through the submission (plan) | Both controllers refuse a branch-scoped actor before the service (403). The central actor's submission is the recorded one, the 403 leaves no ledger row, and the deny rule's match is proved by a simulation and by unit tests: letting the branch manager through would have weakened a guard |
| 43 | Run `scripts/acceptance-verify.ps1` on the local stack (P-11) | It needs PowerShell 7, which the machine does not have and the plan forbids. Its 32 seeded counts were compared by psql (32 of 32); CI runs the script |
| 2, 47 | At most two subagents, no multi-agent review workflows (economy mode) | The owner switched the session's multi-agent mode on during P5. It was used for read-only verification only, still two agents at a time: the rule is about load on the machine, and reading agents put none on Docker |

**Deviations from the plan that the prompt or the inventories settled:** `isSelfApproval` is "the approver created the
request, or is the employee" (with the plan's formula alone the fact is never true when the engine is asked); a self-service
leave stores its working days, computed in HR.Contracts; a new database keeps the governed bindings only; the internal endpoint
answers 400 for a domain out of scope before the per-domain ownership check; the start-up check runs in all fifteen hosts; the
evidence of record is the PR run's artifact, not a committed folder.

**What the reviews changed** (one independent review, then two adversarial verifications of the fixes):

- The start-up inventory now also reads every agency's overrides, each tenant in its own session, and is taken once, after
  every seed of the profile (it ran inside the national seed, before the agencies existed on a first start).
- Inside HR.Core the engine starts a workflow only at the action the catalog gives the entity type; the validator refuses a
  `TriggerWorkflow` rule that can match another action; the allow-list of the internal endpoint carries the action too.
- A self-service leave whose policy question fails for an outage is retried by the bus (a dedicated exception type: the bus
  ignores `InvalidOperationException`), not recorded as a rejection; its reference number is taken once.
- The finance pack's rule equals the national rule it restates; the digests of this plan's sets are pinned.
- `scripts/pol047-validate.ps1` pairs the two parts of a start-up by their place in the log and fails on a part that could
  not be taken; AC-POL-49a asserts the override inventory rule by rule; a sequential acceptance run scans a stack for
  personal data before an overlay stage's successor replaces it.
- From the last verification (it read what had been written in answer to the one before, and the server checks that no run could prove before the deploy): the national part of the inventory says how many templates it read, so "nothing refused" cannot be "nothing read"; the server check decodes the policy page before it compares Arabic text, cancels or deletes what it left open, places its leave past the policy's notice, and counts ledger rows by who asked; PLAN-47's server tour, which now runs on the live national leave policy, discards a draft it leaves; a constant that the secret scanner would have reported is written differently; a template with a version history (the server's `Analytics.ETL`) has its own retirement test; the rules inventory no longer calls competition, exam and indicator rules governed.

## 4. The inventories

### 4.1 The domain reality table, before and after

Before (the study's §2.1 re-checked in P0, on `ec9dca37` and on the server at `8abd5aa7fe93`): twenty national templates, all
with rules written in place by older releases; one domain (`Core.Recruitment`) in Shadow, nineteen Off; five domains with a
call site, fifteen with none; six rules the validator would refuse (four fire a retired workflow, one an unbound code, one
orphan); 15 ledger rows, all `Core.Recruitment`. The full table is in the baseline, §0.3.

After:

| Domain | Owner | Call sites | Mode (Production, Staging, prod-like) | Effective rules | Effects consumed |
|--------|-------|------------|----------------------------------------|-----------------|------------------|
| Assets.Custody | Admin.Logistics | `AssetClassifier` (Classify), `AssetsController` (Create, Assign) | Shadow | 6, version 2 on the server: `R-CLASSIFY-CAPITAL`, `R-CLASSIFY-HIGH-VALUE`, `R-PRIV-CODE`, and `R-PRIV-EVIDENCE`, `R-PRIV-WF`, `R-HIGH-APPROVAL`, the last three now bound to `Assign` | Allow, Deny, SetField (`isPrivileged`), SelectCodePolicy, RequireEvidence, TriggerWorkflow |
| IDCard.Lifecycle | IDCard | `CardRequestsController` (Process) | Shadow | none, version 2 on the server (`R-ID-PROCESS` retired) | Allow, Deny (no rule uses them yet) |
| Contracts.Leave | HR.Contracts | `LeaveRequestHandlers` (Create, Update, Approve), `InboundEventCommands` (the self-service event) | Shadow | 1, version 2 on the server: `R-LEAVE-SELF` (bound to `Approve`, on the fact `isSelfApproval`) | Allow, Deny |
| Core.Recruitment | HR.Core, in process | requisition (SubmitForApproval, Publish, PublishInternal), offer (Issue), committee (SubmitCommittee), appeal (FileAppeal), application (Hire) | Shadow | 10, version 1 unchanged: `R-REQ-SUBMIT`, `R-REQ-PUBLISH`, `R-REQ-PUBLISH-DENY`, `R-OFF-ISSUE`, `R-OFF-EXCEPTION`, `R-OFF-OVERRIDE`, `R-COMMITTEE-SUBMIT`, `R-APPEAL-FILE`, `R-REC-ORG`, `R-REC-HIRE-OFFER` | Allow, Deny, TriggerWorkflow |
| Core.Establishment | HR.Core, in process | `EstablishmentChangeLifecycleService` (SubmitForApproval) | Shadow | 2, version 1 unchanged: `R-EST-SUBMIT` (its code now bound), `R-EST-DENY-BRANCH` (now reachable: the fact is real) | Allow, Deny, TriggerWorkflow |
| The fifteen others | their former owners | none | Off, whatever `DefaultMode` says | none: a final published version with no rules (version 2 on the server, version 6 for `Analytics.ETL`), `RetiredAt` set | none |

### 4.2 Modes and clients

| What | Before | After |
|------|--------|-------|
| `appsettings*.json` with a `PolicyEngine` section | 2 (HR.Core Production and Staging, one key) | 10: HR.Core, HR.Contracts, Admin.Logistics and IDCard, Production and Staging each, plus the prod-like compose on the four services |
| Services that register the enforcement client | 11 | 3: Admin.Logistics, IDCard, HR.Contracts |
| Services left with an unread `Integration__HrCoreBaseUrl` | 3 | 0 (removed from Dev.Analytics, HR.Health, HR.Training) |
| Hosts where the start-up check runs | 12 of 15 | 15 of 15 |
| A domain in Enforce in a production file | 0 | 0, and a test forbids it |

### 4.3 Bindings

A new database gets the seven bindings the catalog's triggers name. An existing database keeps its thirteen rows (nothing
removes a binding) and gets `ESTABLISHMENT-CHANGE-APPROVAL`, unpinned. What each old binding was for is in the baseline, §0.6.

### 4.4 Open drafts and agency overrides

The server had no open draft and no agency override, before and after. The acceptance data keeps, on purpose, the pre-ceiling
overrides of agencies B and C (AC-TEN-48e); the start-up inventory now lists exactly those six rules and nothing else, and
AC-POL-49a asserts it.

## 5. The guards and their own negative tests

| Guard | What it stops | Its own negative |
|-------|---------------|------------------|
| `Pol049PolicyDomainCatalogTests` | a second registry; a governed domain without a call site; a service that registers the client without calling it; a registered external type the engine could start | the test's own planted cases; `scripts/pol049-validate.ps1` fails on each of three planted defects (a mode key, a missing registry row, a registration without a call) |
| The validator with the catalog's capabilities | an effect, a field, a code or an action outside the descriptor; any draft or publication in a domain out of scope | one test per refusal (`DOMAIN_OUT_OF_SCOPE`, `WHEN_ACTION_UNKNOWN`, `WHEN_ACTION_OPERATOR`, `THEN_WORKFLOW_NOT_ALLOWED`) |
| The start-up check | a service that boots with an out-of-scope domain's key not Off | unit tests, and on the stack the boot check of AC-POL-49a (a one-off hr-core-web with `PolicyEngine__IT.Tickets=Shadow` exits non-zero with the key's name) |
| `Pol049EffectiveRulesConformTests` and the start-up inventory | an effective rule the validator refuses | the same test on the data of before the sets finds the six rules; `pol047-validate.ps1` fails on a bad log, an old-format log and a start-up whose part could not be taken (eleven sample logs: two pass, nine fail) |
| `Pol049PolicyModeConsistencyTests` | HR.Core and an owner disagreeing on a mode; a domain in Enforce in a shipped file | planted values in the test |
| `PolicyWorkflowCodeGuard` | a policy instance of another workflow | unit and PostgreSQL tests (the cancel and the refusal commit together; nothing is visible before the commit) and AC-POL-49f |
| `Pol049ShadowLedgerRlsPostgresTests` | a Shadow ledger row in the wrong tenant | a body that names another tenant, a client that does not own the domain and a domain out of scope write no row |
| `Pol049ScopeChangeSetTests` (pinned digests) | an applied set edited | a changed digest fails the test, and the boot |

## 6. Results

| What | Result |
|------|--------|
| Build `-warnaserror` (final tree) | 0 errors, 0 warnings |
| Unit suite | 6012 / 6012 (5915 at the end of P1; the baseline filter of the plan was 620) |
| Integration filter of the plan with Testcontainers (PostgreSQL, RabbitMQ) | 141 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, which needs a built image, as before), 0 failed |
| Validation scripts | `pol049-validate`, `pol047-validate`, `wf043-validate`, `hcm025-validate`, `int037-wiring-check`, `vps/lint-compose`, `int037-render-wiring-doc -Check`, `policy-inventory -Check`: all PASS |
| AC-POL-49a..e and g (`AcPol49EngineScopeTests`, prod-like, group main) | Pass, on a first boot of images built from the final tree |
| AC-POL-49f and the boot check of 49a (`AcPol49EnforceOverlayTests`, stage `prodLike:pol49`) | Pass (2 / 2) |
| The rest of P-11's list, local | prod-like: journey 1/1, main 21/21, `AcInt37` 8/8, `AcMsg40` 8/8, `AcWf45` 8/8, `AcWf46` 9/9, `AcPol47` 1/1; `s2s42`: 9/9 and 1/1; least-privilege: 9/9; lab: 20 passed and AC-09 blocked by design since PLAN-42, journey 1/1, `AcHcm26` 5/5, `AcHcm27` 4/4 |
| `scripts/acceptance-verify.ps1` | not run locally (PowerShell 7); its 32 seeded counts compared by psql, 32 of 32; CI runs it |
| Local CI (`scripts/ci-local.ps1 -SkipTestcontainers`, the Linux container) and the pre-push check | on the first release HEAD `17a9076f`: ok, 89 steps passed, 0 failed, 2 not run (the Testcontainers step and the coverage gate, which needs it), 925 s; pre-push OK. On the HEAD that carries the fix below (`a58e7194`): ok again, 89 steps passed, 0 failed, the same 2 not run; pre-push OK. The Testcontainers step runs on the host, because the VPN of the owner keeps the container from reaching it (allowed since PLAN-48) |
| The Testcontainers step on the host, with the filter of CI, on the tree with the fix below | 211 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, as before), 0 failed, of 212 |
| The PR run (the evidence of record) | PR 47. Its first run (`42974750826`, on `17a9076f`): the six other acceptance stages, the images, CodeQL, E2E, gitleaks, the reuse check and the hardening scan green; **red: the Testcontainers step of the `CI` job** (below), and with it the `meta` stage, whose two tests read that job's result, and the `Acceptance` roll-up. Its second run (`42987351533`, on `a58e7194`): **16 of 16 green**. The Testcontainers step: 211 passed, 1 skipped, 0 failed, of 212; the coverage gate 91.6 % (floor 60 %). The acceptance job: PASS, 81 classes, 276 tests passed; the 4 that failed are the four payroll classes marked not required in `ci-scope.json`, as in the green runs of PLAN-47 and PLAN-48, and the 2 blocked are AC-09 (by design since PLAN-42) and AC-12 (it needs a payroll run). `AcPol49EngineScopeTests` 6 of 6 and `AcPol49EnforceOverlayTests` 2 of 2. Evidence of record: https://github.com/omardefdev/HRManagement/actions/runs/42987351533/artifacts/13842207523 (kept until 2026-12-11) |

One local failure was not a defect: in the first run on the final images `Ac40f` (PLAN-40's test) lost one of its thirteen `GET /metrics` calls to a connection reset. Docker Desktop's own log shows another project's test run creating and deleting published-port containers every few seconds at that moment; the class passed 8 of 8 when it ran again.

One local failure was a defect, and it was misread before the push. The full Testcontainers category on the host had ended with ten failures of `CrossTenantRlsIntegrationTests` (53300, no connection slot left); the class passed alone, 18 of 18, and the failure was put down to the many cores of the host. It was the collection's shared server. Every test takes a database of its own, so a pool of its own; Npgsql keeps an idle pooled connection for five minutes; and this plan's ten new PostgreSQL tests added 21 idle connections to a server of 100 slots that was already near its limit. Measured afterwards: 90 connections, 89 of them idle, at the moment the RLS proofs began, and they take eighteen databases. The PR's first run failed on the same class (five tests). The fix is on the release branch (`3005a21c`): the connection strings of `PostgreSqlFixture` close an idle connection after five seconds, and `PostgreSqlFixtureIdleConnectionTests` proves it on the server itself (it fails with Npgsql's defaults; tried). A connection that is never returned to its pool is not closed by this, so a real leak would still fill the server. Before and after, same filter, same host: 10 failed of 211, then 0 of 212; the shared server's peak 90, then 25; the server of the `PgRabbit` collection 81, then 11 (it was the next to fail). The rule this leaves is in `docs/CI_AND_TESTING.md`: a class that fails in the full step and passes alone is a real failure, because the server is shared and CI runs the whole step.

## 7. The cost of Shadow

Measured by the client itself (`policy_client_call ... elapsed_ms=`, one line per call, and the histogram `policy_client_call_duration_ms`).

| Where | Calls | Milliseconds |
|-------|-------|--------------|
| AC-POL-49b, leave (HR.Contracts to HR.Core), local prod-like stack | 5 | 48, 4, 12, 9, 4: the first call of the service 48, then 4 to 12 |
| AC-POL-49d, custody (Admin.Logistics to HR.Core), local prod-like stack | 4 | 7, 16, 6, 3 |
| The demo server, leave (hr-contracts-web to hr-core-web) | 4 | 76, 6, 14, 11: the first call of the container 76, then 6 to 14 |
| The demo server, custody (admin-logistics-web to hr-core-web) | 8 | 351, 13, 17, 15, 9, 13, 7, 10: the first call of the container 351, then 7 to 17 |
| The demo server, cards (idcard-web to hr-core-web) | 1 ledger row | not read: the container was recreated by a later server check (AC-OPS-39f), and its log, where the client writes its time, went with it |

What a call costs when HR.Core is not well: a refused connection (HR.Core stopped or starting) is immediate, and the action goes on; five of them open the circuit for 30 s. A HR.Core that accepts and does not answer costs the client's timeout, 15 s for each call (an asset creation makes two), and the breaker does not count that timeout; the leave approval waits inside its database transaction. Nothing is refused and no error is added in Shadow. Before Enforce or a real load: a shorter timeout of its own for the policy client and a breaker that counts timeouts.

## 8. The server

**Before** (read-only, 2026-11-11T00:41Z; the same picture as in P0): tag `8abd5aa7fe93`; the PLATFORM tenant only; twenty national templates, each with the rules older releases wrote, nineteen on version 1 and `Analytics.ETL` on version 5; no open draft, no agency override; five seed applications, all NoOp; a `PolicyEngine` section in the Production file of hr-core-web only (`Core.Recruitment: Shadow`); 15 ledger rows, all `Core.Recruitment` in Shadow.

**Changes and their undo:**

| # | Change | Undo |
|---|--------|------|
| 1 | Backup `/opt/hrm/backups/pre-plan49-20261111T004110Z` (700; `pg_dumpall.sql` 28.7 MB, `env.backup`, `SHA256SUMS`: 600). Verified: 17 `CREATE DATABASE` statements for the 17 databases of the server, the completion line of the dump, `sha256sum -c` | none needed; delete it when it is no longer wanted |
| 2 | One detached build of `046316bde9e7` (the P5 commit): upload 32.0 MB in 15 s, build 1223 s, push 17 s, 17 of 17 images in the loopback registry | none needed |
| 3 | Deploy `scripts/vps/deploy.ps1 -Tag 046316bde9e7`, first attempt, 2026-11-11T01:03:34Z, healthy at 01:05:34Z (119.6 s: pull 2, up 92, bootstrap 38, Keycloak reconcile 8, healthy 13; the services were away about 1 min 45 s). Self-test 50 pass, 0 fail, 1 warning (the client-side port probe, as before) | `scripts/vps/deploy.ps1 -Rollback`, no SQL and no restore (the rollback note) |
| 4 | The first start-up: the migration `POL049_PolicyDomainRetirement`, the four change sets (18 applications, all Published), the binding of `ESTABLISHMENT-CHANGE-APPROVAL` | none: a published version is not undone (undoing one is a new version); the previous release runs beside this data |
| 5 | What the server checks leave: from this plan, one leave request (approved, then cancelled; the balance is back at 9 days used) and one freeze request of a vacant position (submitted, then cancelled with its approval instance), both marked «PLAN-49 server check», their numbers, the archive item of the approval, audit rows, and three ledger rows; from the tour of PLAN-47, versions 3 and 4 of the national `Contracts.Leave` policy (a copy, then a rollback to it: the rules are those of version 2); from the checks of PLAN-38 and PLAN-43..46, what they leave at every run (a leave, a card, two requisitions, assets and their approvals, a disciplinary case) | left as they are |

**After** (the first server check, 01:06Z, and the pictures in `artifacts/plan49/vps/`):

- `__EFMigrationsHistory` holds `POL049_PolicyDomainRetirement`; `PolicySeedApplications` holds `POL-049-SCOPE-RETIRE` once for each of the fifteen domains and the three others, each Published.
- The fifteen out-of-scope templates carry `RetiredAt`; the effective version of each is its last one, holds no rule and withdraws all (version 2; version 6 for `Analytics.ETL`, on top of the five it had).
- The governed templates: `Assets.Custody` version 2 (six rules; `R-PRIV-EVIDENCE`, `R-PRIV-WF` and `R-HIGH-APPROVAL` carry `action = Assign`), `IDCard.Lifecycle` version 2 (no rule), `Contracts.Leave` version 2 (`R-LEAVE-SELF` with `action = Approve`), `Core.Recruitment` and `Core.Establishment` version 1, unchanged. No draft is open.
- The start-up inventory: `0 rule(s) of effective versions the validator refuses; 23 more in superseded versions (history, unchanged); 20 national template(s) read`, and `0 rule(s) of overrides in force the validator refuses, in 0 of 0 agency tenant(s) read`.
- Modes as the four running services see them: the five governed domains Shadow in hr-core-web, the own domain of each owner Shadow in hr-contracts-web, admin-logistics-web and idcard-web; nothing in Enforce; no key for a domain out of scope.
- The eight services without the client are running and healthy; hr-health-web, hr-training-web and dev-analytics-web have no `Integration__HrCoreBaseUrl`.
- Ledger rows since the deploy (the release first started on the database at 01:05:03Z), all in Shadow, each domain asked by its owner only:

  | Domain | Caller | Rows | Actions |
  |--------|--------|------|---------|
  | Assets.Custody | `svc-admin-logistics` | 8 | Create 3, Classify 3, Assign 2 |
  | Contracts.Leave | `svc-hr-contracts` | 4 | Create 2, Approve 2 |
  | IDCard.Lifecycle | `svc-idcard` | 1 | Process 1 |
  | Core.Recruitment | none (inside HR.Core) | 2 (15 before the deploy) | SubmitForApproval, Publish |
  | Core.Establishment | none (inside HR.Core) | 1 | SubmitForApproval, with `R-EST-SUBMIT` matched and `R-EST-DENY-BRANCH` not |

- `/ui/hr-core/PolicyTemplates` shows the twenty domains once: five governed in Shadow, fifteen out of scope in Off, each with the reason of the catalog and no draft link.
- The server suites (01:07:52Z to 01:28:26Z, 20 min 33 s, nothing heavy beside them): 34 of 35 passed in the first run: `AcOps36VpsBootTests` a to k without 36j (excepted as before), `AcInt37VpsServerTests` 3 of 3 (the self-test with mandatory wiring among them), and the server checks of PLAN-38, 39, 40, 43, 44, 45, 46 (three of four), 47, 48 and the second check of this plan. The one that failed, `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist`, read the log of idcard-web before its first sweep cycle (the server check AC-OPS-39f had recreated every service at 01:21Z, and the sweep runs every ten minutes), as in PLAN-46, 47 and 48; it passed alone at 01:32Z, after the cycle of 01:31:15Z. The self-test ran once more after all of it: 50 pass, 0 fail, 1 warning; 20 containers healthy; no draft open. The first and the third check of this plan ran alone, the third last: Pass.

**The rollback note** (what the previous tag `8abd5aa7fe93` does with the data of this plan; read from its code with `git show`; also in `docs/OPS_VPS_SINGLE_HOST.md`): the two new columns are nullable and the older model neither reads nor writes them; its applier reads the applications of its own two sets only, both recorded with the same digest, so it re-adds nothing; it answers a version with no rules by its default and no service calls the fifteen domains, but its policy page would let an author open a draft there (do not; a draft left there must be discarded before this release returns); the new versions of the three owner domains stay effective while those domains are Off under the older files, so nothing evaluates them; its compose gives `Integration__HrCoreBaseUrl` back to the three services. So a rollback needs no SQL and no restore.

## 9. Deferred items

- Enforce for any domain, and a Shadow report (G-POL-10, D-POL-13): wave 2.
- Legal values with a legal source, and the legal leave limits (G-POL-12): the local limits stay until the legal owner decides.
- The asset thresholds (10,000 and 5,000) stay in the client's shim.
- OPS-D: jobs and `/careers` under row security.
- AC-09 and AC-10's documents, the PROD-001 documents: PLAN-65.
- The unique index on `PolicyTemplates (TenantId, Domain, Scope)` and the two endpoints that serialise EF graphs: left by
  PLAN-48, not needed here.

## 10. Defects for later plans

1. **Before any domain goes to Enforce:** `RecruitmentAppealService` stages the reviewer's assignment before it asks the
   policy, and the save flushes it. A refusal in Enforce would leave the appeal assigned with no workflow. Latent in Shadow.
2. **Before `Contracts.Leave` goes to Enforce:** a self-service delivery that ends in an engine outage has spent its reference
   number (numbers are not gapless); a stable request key on the number call would remove it. And the bus retries for about
   half a minute only: a longer outage parks the message until an operator replays the error queue.
3. `AcWf46PolicyOrphanTests` (AC-WF-46j) runs with hr-core-web out of Enforce: the class before it in the stage recreates the
   service without the wf46 overlay. Its count still holds; its claim about Enforce does not. PLAN-46's class, not changed here.
4. The pin of `ASSET-ASSIGN-APPROVAL` to a retired definition on a new database is left as it was (nothing reads it); it
   produces a misleading publish warning.
5. `AssetsController` reads `policyResult.WorkflowInstanceId` of the remote answer, which is always empty (the internal
   endpoint starts nothing).
6. `PolicyStaticFallbackShim.GetDecimal` parses with the current culture and `LeaveAndAdvanceRules` formats dates with it
   (older than this plan; the owner's machine runs ar-SA).
7. `S2s042GatewayProofV2Tests.One_hundred_proofs…` is a timing test that failed once under load (5.6 s against 5 s).
8. A hand-pinned `PolicyWorkflowBindings` row can make the engine's instance run another definition than the code names
   (`WorkflowStarter` returns the bound definition without comparing its code). The guard of this plan catches the result;
   the starter itself should compare.
9. A governed domain with no rule (`IDCard.Lifecycle`) is answered by the engine's static default: the ledger row says "no published policy" and names no version, although a published (empty) version exists. Harmless in Shadow; a Shadow report would misread it.
10. The policy client waits its full timeout (15 s) on a HR.Core that hangs, and the circuit breaker does not count timeouts (section 7).
11. `ErrorQueueReplay` has no caller in product code: a self-service leave parked after an outage longer than the bus's retries (about half a minute) waits for an operator.
12. The server check `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist` fails in every full run of the server suites since PLAN-46 and passes alone afterwards: it reads the log of a container that another check of the same class (AC-OPS-39f) has just recreated, before the first sweep cycle (ten minutes). It should wait for a cycle, or the suite should order the two.
13. The server checks write into the committed `acceptance-evidence/` folder, so every plan restores it by hand (known since PLAN-48).
14. The time a card action spends on its Shadow call cannot be read on the server after the suites: the client writes it to the log of the container, and a later check recreates the container. The histogram exists; nothing on the server keeps it.
15. The machine of the owner: Docker Desktop keeps its data disk (245 GB) on drive C, which filled to zero during this plan and killed the Docker engine. Moving the disk image to drive D (262 GB free) or compacting it removes the cause; a setting of the machine, not of the repository.

## 11. Timing

Started 2026-11-10T08:35:40Z; first push 2026-11-11T06:45:45Z, 22 h 10 min after the start; merged 2026-11-11T09:13:33Z. **Total wall-clock: 24 h 38 min**, of which the session stood still for 11 h 2 min without anybody being asked anything (the first row below): about 13 h 36 min of session time. `scripts/plan-timing.ps1 -From 25 -To 49` prints for PLAN-49: 10 commits, started 2026-11-10 11:35, first commit 11:37, end 2026-11-11 04:35 (Riyadh time), span 16:58, wall-clock 17:00. The script takes the merge of the feature branch into `release/plan-49` (`17a9076f`, 01:35Z) for the merge, so its figure stops before the PR.

| What | Time |
|------|------|
| The session stopped, nobody was asked anything | 11 h 2 min in six stops: 17 min (the desktop session ended, 08:43Z), 2 h 51 min (a turn that stopped at 14:45Z until the owner said «Try again»; the machine slept 1 h 41 min of it), 29 min (the owner restarted the machine during the disk incident), 2 h 31 min (the usage limit of the owner's plan, 20:02Z to 22:33Z; an acceptance chain ran by itself for 93 min of it), 9 min (a turn cut at 22:48Z), 4 h 45 min (the usage limit again, 01:42Z to 06:27Z on 2026-11-11: local CI finished by itself in the first 12 min, and a restart by Windows Update kept the machine down for 3 h 5 min) |
| The machine asleep while a run was in progress | 61 min (13:03Z to 14:04Z): the stack start it interrupted failed and was run again |
| Lost to the disk incident and the cold image build after it | about 3 h 20 min: the incident and the recovery (18:40Z to 19:40Z), a chain whose image build failed after 39 min on a slow line and whose later stages then ran on stale images (105 min), the diagnosis and the fix (the NuGet cache filled from the host) |
| The four inventories of P0 | 61 min, beside the first stack start |
| Image builds, local | about 12 min with a warm cache; 15 min after the incident with the cache filled from the host; the cold build over the network failed after 34 min |
| Stack starts, local | prod-like 264 to 306 s without a build, 948 to 1235 s with one |
| The acceptance chain on the final images | 71 min (stage `pol49` 20 min 31 s with the build, prod-like list 14 min 33 s, `s2s42` 7 min 31 s, least-privilege 14 min 6 s, lab 13 min 39 s), and the prod-like list once more after the last fixes, 24 min 31 s |
| The reviews | the independent review 31 min; the second verification about 80 min until the restart cut it; the last verification 80 and 101 min for its two launches; all beside acceptance runs |
| The server | upload 15 s, build 1223 s, push 17 s; deploy 119.6 s to all healthy (the services away about 1 min 45 s); the server suites 20 min 33 s and three short runs |
| The PR, from the first push to the merge | 2 h 28 min: 28 min until the red step of the first run showed; 1 h 15 min from there to the second push (the measurement on the host, the fix and its proof, the documents, local CI 18 min, the pre-push check 20 min; the first run went on for 20 min of it); 43 min for the second run; 2 min from green to the merge. CI ran 1 h 31 min in all (47 min 55 s, red in one step; 43 min 2 s, green) |
| Waiting for CI | about 1 h 11 min: the 28 min of the first run before its red step showed, and the second run |
| GitHub Actions | run 1: 197.6 runner-minutes (205 billed, each job rounded up); run 2: 205.8 (213 billed); the two post-merge runs 3.2 (6 billed: the tree was already tested, so only gitleaks and the hardening scan ran) |

## 12. Readiness note

**For PLAN-65 (AC-09, AC-10, the PROD-001 documents):** the catalog is the place to read what a domain accepts. AC-09, AC-10
and AC-11 run on `Assets.Custody` with the pair the call site sends (`AssetAssignment` + `Assign`); the custody rules with an
effect only the handover reads carry `action = Assign`, so a `Create` or `Classify` evaluation no longer triggers or demands
evidence. `docs/POLICY_DOMAIN_REGISTRY.md` is generated from the catalog and compared with it by a test: edit the catalog,
never the document. The fifteen retired domains have a final version with no rules; bringing one back is a descriptor, a call
site and a change set, never an edit of a version.

**For wave 2 (G-POL-10, D-POL-13):** every governed domain now writes a Shadow ledger row per question, in the caller's
tenant, with the mode, the owner's client id (empty for the two domains inside HR.Core), the entity, the action, the matched
rules, the decision and a hash of the facts. A Shadow report needs: (1) what the local code decided for the same question (the
ledger holds the engine's answer only, so agreement cannot be computed today); (2) the facts themselves or a way back to them
(the ledger keeps a hash); (3) a retention rule for the ledger, which grows by one row per leave, asset and card action;
(4) the two defects above that act only in Enforce.
