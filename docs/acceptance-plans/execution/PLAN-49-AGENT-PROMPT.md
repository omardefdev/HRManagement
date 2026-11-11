# Task: PLAN-49 — What the policy engine governs, facts that match their schemas, and no dead or orphan rules

The deliverables:
- **One domain catalog in code** (`PolicyDomainCatalog`). For each of the 20 domains it records:
  - its status (`Governed` or `OutOfScope`) and its owner;
  - its entity types and their actions;
  - the effects it accepts, the fields a `SetField` may write, and the workflow codes a rule may name;
  - its call sites, or the reason it is out of scope.

  The catalog drives the validator, the engine, the internal endpoint, the start-up check, the policy page and `POLICY_DOMAIN_REGISTRY.md`. There is no second registry.
- **Five governed domains in Shadow** in Production, Staging and prod-like: `Core.Recruitment`, `Core.Establishment`, `Assets.Custody`, `IDCard.Lifecycle`, `Contracts.Leave`. Each has a proved call site and live ledger rows.
- **The fifteen other domains retired** by a final version with no rules, and documented. The enforcement client stays only in the services that call it.
- **No effective rule** with an effect nobody consumes, a placeholder legal value, or an orphan `TriggerWorkflow`.
- **Facts that match their schemas.** Leave passes the engine on create, update, approve and ESS. Establishment sends the actor's real scope.
- **A policy may refuse an approval, or start the workflow the service asked for — never another one** (G-POL-09).
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks.

**Standing owner instruction on merges** (PLAN-38 to PLAN-48 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-15).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan49-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan49-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan49-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time spent waiting for the owner** (the target is zero).
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in the Claude desktop app, at `xhigh`):
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures. **No multi-agent review workflows.**
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan49/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Waiting — for CI and for your own work (changed after PLAN-48).** PLAN-48 sat idle for 3 h 07 min after a red CI run, until the owner turned on the app's Auto-fix. It then waited about an hour for the owner's «continue» after its own review. So:
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A background `gh run watch <id> --exit-status` is fine too, if this session's background commands wake it when they end.
- **Then act at once:**
  - every check green → merge (P-15);
  - a red check → fix it on the release branch, push that branch only, and wait again the same way.
- **The app's controls:** if the owner turns on **Auto-fix** for the PR, its wake-ups are welcome, but they do not replace your waiting. Never use **Merge when ready**: it squashes, and you merge with `gh pr merge <n> --merge`.
- **`WAITING-CI:`** is only for a session that cannot run commands at all:
  - end the turn with a line that starts with `WAITING-CI:` and names the PR, the run id, its URL and the exact next step;
  - on the owner's next message (even «اكمل»), check **once** (`gh pr checks <n>`) and continue from that step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for policy engines on multi-tenant platforms:
- domain catalogs and capabilities, Off/Shadow/Enforce semantics, decision ledgers, seed change sets and immutable published versions;
- approval workflow coordination (start, reuse, cancel) and instance integrity;
- EF Core, PostgreSQL row level security (`FORCE`, non-owner roles) and migrations;
- service-to-service calls (client credentials, tenant headers), MassTransit consumers, and the outbox and inbox of PLAN-40;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..48 did.

**Where things stand** when this prompt was written (2026-11-10):
- **`main`** = `f99950bf`. It holds PLAN-48 (PR 46).
- **PLAN-48's merge note** is a single commit, `ea91a632`, on the local branch `docs/plan-48-merge-note`, not yet on `main`. The main folder is checked out on that branch.
- **The demo server** runs `8abd5aa7fe93`: PLAN-48 without its CI fix `95228ffa`, whose only server-side change is one HR.Core log line. This plan's deploy brings it.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI since PLAN-66:**
  - a full PR run takes about 45–50 minutes, and one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs; a group with overlays lists them in `order` (`composeFiles`, `recreate`), as `s2s42` does.
- **From PLAN-48** (its final report, §3 and §7–§12):
  - **The national read path:** `OwnOrNational`, `NationalOnly`, `OwnFirst` (ServiceDefaults) and `NationalReadQueries` (HR.Core) are the only way to read a national table. `national_shared_read` lets agencies read under forced row security. `NationalSharedWriteGuard` refuses an agency's write to a national row (403).
  - **The ceiling:** `PolicyCeiling`, `PolicyRuleDefinition.Origin`, and the validator's `IsAgencyOverride`, `NationalSetFields` and `NationalTriggersWorkflow`. `IPolicyRuleValidationService` takes the template's scope on every call.
  - **Left for this plan:** the retired triggers (R-HIRE-START, R-SEP-START, R-TKT, R-TRF) and R-EST-SUBMIT's missing binding. The start-up inventory lists them: `PolicyPublishedRuleInventory`, log prefix `PLAN-47 validator inventory`, read by `scripts/pol047-validate.ps1`.
  - **Agency seeding** goes through the publish service (`AgencyPolicySeeder`, `PolicyDraftService.CreateDomainDraftAsync`).
  - **Files PLAN-48 shares with this plan:** `PolicyOrchestrator.cs`, `PolicyEvaluator.cs`, `PolicyRuleValidator.cs`, `PolicyRuleValidationService.cs`, `PolicyDraftService.cs`, `PolicyPublishService.cs`, `PolicySeedChangeSets.cs`, `WorkflowPackSeeder.cs`, `PolicyEngineSeeder.cs`, `AcceptanceDataSeeder.cs` (custody v2), and the gateway's policy rules (`GatewayPathAuthorizationMiddleware.cs`).
  - **Known defects that are not this plan's,** unless its tests need them:
    - `GET api/codes/policies` and `GET api/policies/templates/{id}` serialise EF graphs (a JSON cycle, a 500);
    - `PolicyTemplates (TenantId, Domain, Scope)` has no unique index.

    If a test of this plan needs one of those endpoints, return a DTO (never the EF graph) and record it.
  - **The ClamAV host port 53310:**
    - on this machine, the acceptance stack could not start, because Windows reserved TCP 53248-53347;
    - on GitHub runners, the bind collides in about 6% of stack starts.

    P0 fixes it (P-2).
- **The code today** (read on `f99950bf`). The plan's line numbers date from 2026-10-30 and have moved since PLAN-42..48: find the code by its content.
  - **Capabilities (PLAN-47):** `IPolicyDomainCapabilities` (HR.Core.Domain) has `AllowsEffect`, `AllowsSetField` and `AllowsWorkflowCode`.
    - `AllowAllPolicyDomainCapabilities` is registered in `PolicyEngineDependencyInjection`.
    - `PolicySeedChangeSetApplier.Create` builds its own validation with it when no container is at hand.
  - **The PLAN-42 maps:**
    - `PolicyDomainOwners` (SharedKernel) maps 17 domains to an owner client; `Core.*` has no entry.
    - `PolicyWorkflowTriggerAllowlist` (HR.Core) has been empty since PLAN-43. The internal endpoint starts no workflow; an owner starts its approvals through `ExternalApprovalService`.
  - **Modes:**
    - `PolicyModeResolver` reads `PolicyEngine:{domain}`, then `PolicyEngine:DefaultMode`, then Off. `DefaultMode` is set nowhere today.
    - `PolicyEngineConfigurationValidator` refuses:
      - an unknown key or mode;
      - `AllowWithAlert` in Production (D-POL-2);
      - an active domain without `Integration:HrCoreBaseUrl`, on a remote client outside Development.
  - **Where modes are set:**
    - Only HR.Core's Production and Staging files set `Core.Recruitment=Shadow`. The prod-like compose repeats it, on hr-core-web alone.
    - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. Its compose mirrors the Production values it needs.
    - The lab compose sets `Assets.Custody=Enforce` (AC-10, AC-11).
    - The `s2s42` group's overlays set:
      - `Assets.Custody` (`docker-compose.acceptance-s2s42.yml`, with `Seed__S2s42PolicyPatch`);
      - `IDCard.Lifecycle=Enforce` (`docker-compose.acceptance-wf46.yml`, for AC-WF-46j).
    - Admin.Logistics and IDCard have no `appsettings.Staging.json`.
  - **The client** is registered in 11 services.
    - It stays in Admin.Logistics, IDCard and HR.Contracts, which call it.
    - It goes from Dev.Analytics, IT.Infra, Payroll, HR.Movements, Records.Archive, Compliance, HR.Health and HR.Training.
    - HR.Core has had none since PLAN-37.
    - Every owner service has `Integration__HrCoreBaseUrl` on every compose chain: hr-contracts-web in `docker-compose.yml`, the others in `docker-compose.services.yml`.
  - **Call sites:**
    - Admin.Logistics: `AssetClassifier`, and `AssetsController` (two calls);
    - IDCard: `CardRequestsController`;
    - HR.Contracts: `LeaveRequestHandlers`, on create only;
    - HR.Core:
      - direct calls in `RecruitmentRequisitionLifecycleService` (two), `RecruitmentAppealService` and `RecruitmentApplicationsController`;
      - through `ApprovalStartCoordinator`: the requisition, offer, committee and appeal services, and the establishment submit in `EstablishmentChangeLifecycleService`. The submit sends `hasOrgScope: false`; the restart path asks the engine nothing;
      - `ExternalApprovalService` also starts through the coordinator. Its types never get a policy instance (PLAN-43).
    - `PolicyDomainGateway` (ServiceDefaults) has no caller.
  - **Change sets (PLAN-47):** `AddRule`, `ReplaceRule`, `RetireRule`, `EnsureFactsField`.
    - They apply in one transaction per template, under an advisory lock.
    - A template with an open draft is skipped until the draft is published or discarded.
    - A domain with no national template logs a warning and gets nothing.
    - An applied set never changes: its digest is stored, and a changed digest fails the boot.
    - `PolicyEngine:Seeds:RequireApproval` is set nowhere, so sets publish as `SystemSeed`.
    - The baseline set `POL-2026-10-30-BASELINE` also touches `Archive.Retention` (R-ARC-HOLD and its `legalHold` field).
  - **Bindings:**
    - `PolicyEngineSeeder` binds:
      - ASSET-ASSIGN-APPROVAL and ASSET-LOSS-APPROVAL;
      - ID-CARD-ISSUANCE;
      - HIRE-WIZARD, SEPARATION-WIZARD and TRANSFER-APPROVAL;
      - the requisition and both offer codes;
      - DISCIPLINARY-APPROVAL and IT-TICKET;
      - the committee and appeal codes.
    - ESTABLISHMENT-CHANGE-APPROVAL (R-EST-SUBMIT's code) has no binding.
    - `WorkflowStarter` pins a definition through a binding; with none, it takes the latest published definition.
    - `PolicyRuleValidationService` reads bindings for `TriggerWorkflow` codes.
  - **The orchestrator** starts a policy workflow only in Enforce, only for an internal caller, and never for a type in `ExternalApprovalTypes` (PLAN-43).
  - **The coordinator:**
    - `ApprovalStartCoordinator` returns the policy's instance without comparing its code (G-POL-09).
    - `FindReusableInstanceAsync` already compares codes and reads only `InProgress` instances.
    - The override code is `OfferOverrideApprovalRules.WorkflowCode`.
  - **Tests that touch this plan:**
    - AC-09, AC-10 and AC-11 use `Assets.Custody`.
    - `AcWf46PolicyOrphanTests` (AC-WF-46j) counts instances around a card processed in Enforce. It does not assert that R-ID-PROCESS matched.
    - `AcInt37ProductionWiringTests` drives the ESS leave as `employee-a`. HR.Core's `EssPortalController` publishes `ILeaveRequested`, and HR.Contracts' `ProcessLeaveRequestedHandler` consumes it.
  - **Personas:** every persona the plan names exists. There is no persona change, so `DATA-AUTH` stays at 65.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43).
  - **A job or a start-up step that reads several tenants** must walk them from a source outside row security and set `TenantIdFlow` for each (PLAN-44, PLAN-45, PLAN-48). `IgnoreTenantFilterForSystem` lifts only the EF filter, never row security.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-43, PLAN-44, PLAN-47, PLAN-48).
  - **Who connects how:** the demo server and the default acceptance stacks connect HR.Core as the PostgreSQL superuser; production and the least-privilege stack connect as `hr_core_app`.
  - **Personas and users:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (PLAN-43);
    - a new persona also changes `DATA-AUTH` in `scripts/acceptance-verify.ps1` (PLAN-48);
    - a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh` (PLAN-45).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Acceptance tests:**
    - a test posts the URL and the token the page renders (PLAN-44);
    - policy API writes need the antiforgery token (`AcceptanceHarness.CreatePolicyLifecycleClientAsync`, PLAN-47).
  - **The server:**
    - run the server build detached (PLAN-44);
    - run the server suites with nothing heavy beside them (PLAN-45);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan49/vps/` (PLAN-48).
  - **Before the push** (PLAN-48's first run was red for these):
    - run `scripts/acceptance-verify.ps1` on your local stack: CI runs it at every stage;
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging`;
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it, and never quote a flagged value in a report.
  - **When the owner's VPN is on,** ci-local's Testcontainers fail with "Failed to connect to 192.168.65.254". Run those classes on the host, then `ci-local.ps1 -SkipTestcontainers`. That is allowed, because Testcontainers run on ubuntu-latest in CI (PLAN-48).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-49-pol-engine-scope-orphans.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. These reports:
   - `reports/plan48-final-report.md`, in full;
   - `reports/plan47-final-report.md`: the validator, the change sets, the inventory;
   - the policy parts of `reports/plan42-final-report.md`: Shadow semantics, the internal endpoint, the owners;
   - the policy parts of `reports/plan46-final-report.md`: the orphan definitions, the cross-service approvals.
3. These documents:
   - `docs/POL_001_GAP_STUDY.md`: G-POL-02, G-POL-04, G-POL-09, G-POL-11, and the matrix of §2.1;
   - `docs/POLICY_DOMAIN_REGISTRY.md`, `docs/POLICY_ENGINE_UAT.md`, `docs/policy-rules-inventory.csv`;
   - the PROD-001 card in `docs/completion-backlog/07-NATIONAL-PRODUCT-MIGRATION-INTEGRATION.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
6. `docs/OWNER_DECISIONS_REGISTER.md`: D-POL-1, D-POL-2, D-POL-4, D-POL-5, D-POL-13, D-POL-14.
7. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`,** on `main` or on `docs/plan-48-merge-note` (where PLAN-48 left the folder), and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-48 (`f99950bf` or later).
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

Accepted by the owner on 2026-10-31 (wave-1 questionnaire, «accept the recommended» for every plan decision). The plan's defaults apply as written:

- **D-POL-1, what the engine governs:**
  - **Governed, in Shadow:** `Core.Recruitment`, `Core.Establishment`, `Assets.Custody`, `IDCard.Lifecycle`, `Contracts.Leave`.
  - **Out of scope, retired and documented:** `Assets.Procurement`, `Contracts.Advance`, `Contracts.Hire`, `Contracts.Separation`, `Contracts.Contract`, `Movements.Transfer`, `Movements.Workforce`, `Core.Employment`, `Health.Insurance`, `Compliance.Disciplinary`, `Payroll.Period`, `IT.Tickets`, `Archive.Retention`, `Training.Learning`, `Analytics.ETL`.
  - Extending later means a descriptor, a call site and a change set.
- **D-POL-5 (the leave part):** no placeholder value in the engine. `R-LEAVE-MAX` and `R-LEAVE-LONG` are retired. The local limits (`GetMaxDuration`, `LeavePolicy`) stay as they are until the legal owner decides from the official text (`docs/syr/legal-source-binding.md`).
- **D-POL-13:** no move from Shadow to Enforce in this plan, and no domain in Enforce in any production file.
- **D-POL-14:** an ESS leave request that the engine refuses in Enforce is created `Rejected`, with the policy's reason in `Notes`. It is never dropped. It is built now and acts only in Enforce.
- **Still in force:** D-POL-2 (no `AllowWithAlert` in Production) and D-POL-4 (PLAN-48's agency ceiling).

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-49-pol-engine-scope-orphans` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-48's merge note from `docs/plan-48-merge-note` (`ea91a632`);<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **Docker hygiene, and the ClamAV port.**<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run (`ci-local.ps1`, Testcontainers suites, a stack), check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*` and Testcontainers.<br>• Never prune volumes or images of other projects.<br>• **The ClamAV host port (PLAN-48 §10), first thing in P0:**<br>&nbsp;&nbsp;– `docker-compose.acceptance.yml` maps ClamAV to host port 53310. That port is inside Linux's ephemeral range and inside a range Windows reserved on this machine.<br>&nbsp;&nbsp;– No script, test or workflow reads 53310 (checked on `f99950bf`).<br>&nbsp;&nbsp;– Drop the host mapping. If a check turns out to need one, use 6350, after checking that no compose file uses it.<br>&nbsp;&nbsp;– Make it its own commit; the final report records it as the fix of PLAN-48's defect.<br>• **Before each stack start,** compare the stack's host ports with `netsh interface ipv4 show excludedportrange protocol=tcp`. If another port the tests read is reserved, freeing it is a system change for the owner: `BLOCKED:` with the range. |
| P-3 | **The catalog (plan §1).**<br>• **The model:** `PolicyDomainCatalog` and `PolicyDomainDescriptor` in SharedKernel, one descriptor per entry of `PolicyDomains.All`, with the plan's fields. D-POL-1 decides the statuses.<br>• **Derived, never duplicated:** `PolicyDomainOwners` and `PolicyWorkflowTriggerAllowlist` read the catalog.<br>&nbsp;&nbsp;– The allowlist keeps PLAN-43's meaning: the internal endpoint starts nothing. A test proves that the derived list is empty.<br>&nbsp;&nbsp;– A descriptor's `WorkflowTriggers` say which codes a rule may name for an entity type. The coordinator (P-7) still demands the exact code the service asked for.<br>• **Capabilities:** `CatalogPolicyDomainCapabilities` implements PLAN-47's `IPolicyDomainCapabilities`.<br>&nbsp;&nbsp;– It replaces `AllowAllPolicyDomainCapabilities` everywhere, `PolicySeedChangeSetApplier.Create` and the test helpers included.<br>&nbsp;&nbsp;– Extend the interface where the catalog needs more: the domain's status, the entity type of a `TriggerWorkflow`, the action a rule's `action` condition names.<br>&nbsp;&nbsp;– The validator stays the only place that refuses. It refuses an effect, a field, a code or an action outside the descriptor, and any draft or publish in an `OutOfScope` domain.<br>&nbsp;&nbsp;– An agency rule must pass both the capabilities and PLAN-48's ceiling.<br>• **Run time:** for an `OutOfScope` domain:<br>&nbsp;&nbsp;– `PolicyOrchestrator` returns a neutral decision («المجال خارج نطاق المحرك») with no ledger row and no workflow;<br>&nbsp;&nbsp;– the internal endpoint returns 400;<br>&nbsp;&nbsp;– `PolicyModeResolver` answers Off, whatever `PolicyEngine:DefaultMode` says.<br>• **Start-up:** `PolicyEngineConfigurationValidator` fails the boot of any service where an `OutOfScope` domain's key is not Off. It names the key, never a value.<br>• **The page:** `PolicyDomainRegistry` goes, and the table comes from the catalog, as the plan says. `CreateDraft` refuses an `OutOfScope` domain on the page and in the API (`POST api/policies/drafts` included).<br>• **The clients:**<br>&nbsp;&nbsp;– Remove the registration from the eight services.<br>&nbsp;&nbsp;– Remove an address key only where no other client reads it. HR.Movements' lookup client stays (PLAN-37, PLAN-50).<br>&nbsp;&nbsp;– Update `deploy/wiring/service-wiring.json`. `ProductionWiringMatrixTests`, `scripts/int037-wiring-check.ps1` and `scripts/vps/lint-compose.ps1` stay green.<br>&nbsp;&nbsp;– Never remove a Keycloak client, scope or secret.<br>• **The guard:** `Pol049PolicyDomainCatalogTests`, as the plan says, with one adjustment. In HR.Core the `Core.*` domains reach the engine through `ApprovalStartCoordinator`, so a coordinator call that names the domain counts as a call site.<br>• **`PolicyDomainGateway`** has no caller: delete it if nothing, tests included, uses it, and record it. |
| P-4 | **The change sets (plan §2).**<br>• **The sets,** through PLAN-47's applier: `POL-049-SCOPE-RETIRE` (the new operation `RetireDomain(reasonAr)`), `POL-049-ASSETS-ACTION`, `POL-049-IDCARD-ORPHAN` and `POL-049-LEAVE`.<br>&nbsp;&nbsp;– Each produces a new published version. No version is edited in place.<br>&nbsp;&nbsp;– An applied set never changes (its digest). The baseline set stays as it is, its `Archive.Retention` part included.<br>• **On a fresh database** the applier passes over an `OutOfScope` domain quietly, with no warning at every start: the catalog says why there is no template.<br>• **The migration** `POL049_PolicyDomainRetirement` only adds `RetiredAt` and `RetiredReason`: no data change in a migration (PLAN-43). If a PLAN-47 trigger guards the template row, extend it in the same migration.<br>• **Open drafts:** the applier skips a template with an open draft.<br>&nbsp;&nbsp;– P0 lists the open drafts on every template these sets touch, in the acceptance data and on the server.<br>&nbsp;&nbsp;– A draft left by an earlier plan's server check may be discarded through PLAN-47's discard path; record it.<br>&nbsp;&nbsp;– Any other draft on the server is someone's work: Stop.<br>• **Agency overrides in a retired domain** are inert at run time (P-3).<br>&nbsp;&nbsp;– P0 lists them: acceptance data, agency packs, server.<br>&nbsp;&nbsp;– If any exist, retire them with the same operation. Walk the tenants from the `Tenants` table with `TenantIdFlow` (the PLAN-44 and PLAN-48 pattern).<br>&nbsp;&nbsp;– Drop the retired domains from the agency packs.<br>&nbsp;&nbsp;– The server has none (PLAN-48 §7).<br>• **R-EST-SUBMIT:**<br>&nbsp;&nbsp;– Add the ESTABLISHMENT-CHANGE-APPROVAL binding it lacks: in the fresh seed, and idempotently on existing databases. First check that the definition (`WorkflowPackSeeder`) has a handler.<br>&nbsp;&nbsp;– If it has none, retire the rule in a change set instead. Record which.<br>• **Rule 1 of plan §2 is proved twice:**<br>&nbsp;&nbsp;– by `Pol049EffectiveRulesConformTests`, on a seeded and applied database;<br>&nbsp;&nbsp;– by the start-up inventory, which must list no rule of an effective version, on the stacks and on the server.<br>&nbsp;&nbsp;Update `scripts/pol047-validate.ps1` to that expectation, without loosening anything else it checks.<br>• `WorkflowDefinitionsHaveHandlersTests` takes the plan's added condition. |
| P-5 | **The fresh seed (plan §3).**<br>• `SeedAllDomainsAsync` seeds only the five governed domains.<br>• `Pol049FreshSeedTests`: five templates, and every rule passes the validator with the catalog's capabilities.<br>• **Bindings on a fresh database:**<br>&nbsp;&nbsp;– the codes a governed rule names: ASSET-ASSIGN-APPROVAL, the recruitment codes, ESTABLISHMENT-CHANGE-APPROVAL;<br>&nbsp;&nbsp;– plus any binding that a PLAN-46 cross-service start or a test reads.<br>&nbsp;&nbsp;Before you drop any binding, check ID-CARD-ISSUANCE, DISCIPLINARY-APPROVAL and ASSET-LOSS-APPROVAL against `ExternalApprovalService`, `WorkflowStarter`, `WorkflowDefinitionLifecycleService` and AC-WF-46.<br>• **Existing databases keep every binding.** Record what each binding is for. |
| P-6 | **Facts and the leave channels (plan §4).**<br>• **As the plan says:** the builders; the leave update, approval and ESS paths; establishment's `actorIsBranchScoped`, passed from both controllers the plan names. Remove `ForAdvanceRequest` only after `rg` shows no caller left.<br>• **ESS:**<br>&nbsp;&nbsp;– Run `ValidateLeaveRequest`, then `EnforceAsync` with `action=Create`.<br>&nbsp;&nbsp;– A local refusal, or an Enforce refusal (D-POL-14), creates the request `Rejected`, with the reason in `Notes`.<br>&nbsp;&nbsp;– The tenant comes from `TenantIdFlow`, which the consumer filter sets.<br>&nbsp;&nbsp;– PLAN-40's inbox still deduplicates: a redelivered event creates neither a second request nor a second ledger row.<br>• **A behaviour change to watch:** the ESS channel now runs the local validation it used to skip. If the validation now refuses a test's ESS request (AC-INT-37, the journeys), the test's data changes, never its assertion; record it.<br>• `Pol049FactsBuildersMatchSchemasTests`, as the plan says. |
| P-7 | **The coordinator (plan §6).**<br>• When the policy returns an instance, read its definition code. If it differs from `request.WorkflowCode`:<br>&nbsp;&nbsp;– cancel that instance the way PLAN-44 and `ApprovalRepairService` cancel one (the status, a history entry, the audit);<br>&nbsp;&nbsp;– return «السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء»;<br>&nbsp;&nbsp;– the entity does not change.<br>• **One transaction:** the cancel and the refusal commit together in `StartAndApplyAsync`'s transaction, with the ledger row. Check what the start wrote (history, outbox, notifications), so that nothing announces an instance that was cancelled.<br>• A cancelled instance is never reused and never shown in the inbox.<br>• `ExternalApprovalService` goes through the same coordinator. Its types never get a policy instance (PLAN-43), so the new check never cancels one of their approvals; a test proves it.<br>• **Tests:** `Pol049CoordinatorWorkflowCodeTests` and `Pol049CoordinatorCancelPostgresTests`. |
| P-8 | **Production modes, and Shadow under row security (plan §5).**<br>• **The keys:** the plan's files, plus an `appsettings.Staging.json` for Admin.Logistics and for IDCard, created with only the `PolicyEngine` section, so that the owner's value equals HR.Core's in Staging too.<br>• **The prod-like compose** sets the keys on hr-core-web, admin-logistics-web, idcard-web and hr-contracts-web. Update its header comment.<br>• **Consistency:** `Pol049PolicyModeConsistencyTests`, as the plan says.<br>&nbsp;&nbsp;– `scripts/hcm025-validate.ps1` keeps its `Core.Recruitment` check. Adjust it only where it assumed no other key.<br>&nbsp;&nbsp;– No domain in Enforce in any production file.<br>• **Shadow under row security:** production connects HR.Core as `hr_core_app` under forced row security, and the internal endpoint writes each ledger row in the caller's tenant. Prove it as a non-owner role:<br>&nbsp;&nbsp;– in the `leastpriv` stage, if it can carry one more class: `AcPol49LeastPrivilegeTests`, with one Shadow call per owner service and the ledger row in the caller's tenant;<br>&nbsp;&nbsp;– otherwise, in an integration test of the internal endpoint as a non-owner role without `BYPASSRLS`.<br>&nbsp;&nbsp;Record which. |
| P-9 | **Existing tests that the new rules change.**<br>• A test changes to the new rule if it relied on:<br>&nbsp;&nbsp;– a retired domain or rule;<br>&nbsp;&nbsp;– the client in a service that no longer has it;<br>&nbsp;&nbsp;– `ForAdvanceRequest`, or `PolicyDomainRegistry`.<br>&nbsp;&nbsp;Never weaken an assertion.<br>• **AC-WF-46j** keeps its count. Its evidence text may now say that PLAN-49 retired the card rule.<br>• **AC-09, AC-10, AC-11** (`Assets.Custody`): the `action` condition must not change what they prove; record any data change. PLAN-65 owns their documents.<br>• **AC-HCM-25's assertions and `Hcm025PanelServerParityTests` do not change.** |
| P-10 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• **`AcPol49EngineScopeTests`** (AC-POL-49a..e and g): `prodLike[]`, `required: true`, group `main`. It must not stop or recreate containers.<br>• **`AcPol49EnforceOverlayTests`** (AC-POL-49f, and the boot check of 49a): `required: true`, in a new group `pol49` of `order`.<br>&nbsp;&nbsp;– The group comes after `s2s42`, on stack `prodlike`, with `composeFiles: ["docker-compose.acceptance-pol49.yml"]` and `recreate: ["hr-core-web"]`.<br>&nbsp;&nbsp;– Add `prodLike:pol49` to the `prodlike` stage job, after `prodLike:s2s42`.<br>• **`ACC-POL49-WEAK-OFFER`** applies only when the overlay sets its own seed flag, following `AcceptanceS2s42PolicyPatch` and `Seed__S2s42PolicyPatch`. It is acceptance seeding:<br>&nbsp;&nbsp;– it is never a member of `PolicySeedChangeSets.All`;<br>&nbsp;&nbsp;– it is never present on the main stack.<br>• **The boot check** (`scripts/pol049-outofscope-mode-boot-check.ps1`) proves that hr-core-web refuses to start with `PolicyEngine__IT.Tickets=Shadow`: a non-zero exit and the message. It leaves the running stack as it found it; a one-off container (`docker compose run --rm --no-deps`) does that.<br>• **Evidence ids:** each scenario id appears once. If a scenario spans both classes, its second part gets its own id, registered in `ac-ownership.json`.<br>• `ac-ownership.json`: `AC-POL-49` (owner PLAN-49, `provesClaim: true`).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-POL-49/` as the plan says. Copy what the report needs into `artifacts/plan49/`, then restore the committed evidence. |
| P-11 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcPol49EngineScopeTests`, `AcHcm25ProductionApprovalTests`, `AcWf43DefinitionGovernanceTests`, `AcTen48NationalScopeTests`, `AcPol47RuleLifecycleTests`, `AcWf45AssetApprovalLoopTests`, `AcWf46CrossServiceApprovalTests`, `AcInt37ProductionWiringTests`, and the prod-like journey;<br>• **the groups `s2s42` and `pol49`:** `AcS2s42TrustBoundaryTests`, `AcWf46PolicyOrphanTests`, `AcPol49EnforceOverlayTests`;<br>• **lab:** `Ac09ShadowPolicyTests`, `Ac10EnforcePolicyTests`, `Ac11PolicyWorkflowTests`, `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcHcm21RequisitionApprovalUiTests`, `AcWf23WorkInboxUiTests`, the lab `isolated` group (`AcHcm26`, `AcHcm27`), and the lab journey;<br>• **leastpriv:** `AcTen48LeastPrivilegeTests`, and P-8's class if you add it;<br>• and any class that P0 finds calling the engine or reading a policy template.<br>Then run `scripts/acceptance-verify.ps1` on the stack. Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-12 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• A script that compares Arabic text reads the files as UTF-8 and holds no Arabic literal.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`, not with the plan's raw `docker compose` lines. |
| P-13 | **The demo server facts this plan must respect.**<br>• It connects HR.Core as the superuser, so row security is not enforced there. P-8's proof lives in the tests.<br>• It runs the Production environment. After the deploy, HR.Contracts, Admin.Logistics and IDCard call HR.Core in Shadow on every leave, asset and card action. A Shadow call adds latency and never blocks (PLAN-42).<br>• It has the PLATFORM tenant only, 20 national templates and no override (PLAN-48 §7).<br>• **P0 lists, read-only:**<br>&nbsp;&nbsp;– the effective version and rule keys of every template;<br>&nbsp;&nbsp;– the open drafts;<br>&nbsp;&nbsp;– the ledger rows per domain and mode. |
| P-14 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan49-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-13's picture, and the `PolicyEngine` keys of the four services as their running containers see them.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`.<br>• **Rollback safety:** find out what PLAN-48's tag (`8abd5aa7fe93`) does with this plan's data: the retired templates and their empty final versions, the new columns, the applied sets. Record it in `OPS_VPS_SINGLE_HOST.md`.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-48's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-15 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`, then wait in the session («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-16 | **No regression of PLAN-25, PLAN-36..48 and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests` (P-9);<br>• `AcPol47`, both `AcTen48` classes, `AcWf43`..`AcWf46` with the orphan class, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• the `Pol047*`, `Ten048*` and `S2s042*` tests, `PolicyCeilingTests`, `NationalSharedArchitectureTests`, `WorkflowDefinitionsHaveHandlersTests`, `ProductionWiringMatrixTests`, `OutboxPublishOrderingArchitectureTests`, `Data001ConcurrencyArchitectureTests`, `GatewayPathAuthorizationMiddlewareTests`, `Qa066*`;<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan, and from PLAN-48):
- **Done earlier:**
  - PLAN-42: the fail-closed client, Shadow semantics, the internal endpoint;
  - PLAN-46: cross-service approvals, orphan definitions and their allowlist;
  - PLAN-47: the validator, four eyes, version immutability, the change-set mechanism;
  - PLAN-48: the national read and the agency ceiling.
- **Wave 2:**
  - Enforce for any domain, and a meaningful Shadow ledger (G-POL-10);
  - legal values with `legal_source_id`, and the legal leave limits (G-POL-12; separation types in PLAN-61);
  - moving the asset thresholds (10,000/5,000) from the shim to rules;
  - OPS-D: jobs and `/careers` under row security (PLAN-48 §8).
- **PLAN-65:** AC-09/AC-10, the PROD-001 documents, the thesis and the demo guide.
- **Left by PLAN-48** (P-3 and «Where things stand»): the unique index on `PolicyTemplates`, and the JSON-cycle endpoints.

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- edit an applied change set, or change a published version in place;
- put a domain in Enforce in a production file (Production, Staging, prod-like, the VPS chain);
- remove a Keycloak client, scope or secret;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **An open draft on the server blocks a change set,** and no earlier plan's check left it (P-4).
- **Retiring a domain, a rule or a binding would take away something a running flow needs** (a definition or a binding a PLAN-46 path starts).
- **Shadow cannot write its ledger row in the caller's tenant** under forced row security without new trust between services (P-8).
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning (P-9).
- **A host port the tests read, other than ClamAV's, sits in a reserved Windows range** (P-2).
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, preconditions, the ClamAV port, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-49-pol-engine-scope-orphans origin/main`. Bring in PLAN-48's merge note and commit this prompt file (P-1).
3. **The ClamAV port** (P-2). Commit: `fix(qa): the acceptance stack no longer binds ClamAV to host port 53310 (PLAN-48 §10)`.
4. **The plan's baseline** («المرحلة 0») into `reports/plan49-baseline.md`:
   - **0.2:** the build, and the listed filters (Testcontainers included);
   - **0.3:** the reality table, domain × call site × effective rules × effects, from the code and the acceptance database, against the study's §2.1;
   - **0.4:** what PLAN-46, PLAN-47 and PLAN-48 delivered that this plan uses.
5. **Inventories:**
   - every `PolicyEngine` key in every appsettings file, compose file, overlay, the VPS chain, the test fixtures and the scripts;
   - the client registrations, their address keys, and what else reads those keys;
   - every test or script that names an out-of-scope domain, a retired rule, `ForAdvanceRequest`, `PolicyDomainRegistry`, `AllowAllPolicyDomainCapabilities` or `PolicyDomainGateway`;
   - the bindings and their readers (P-5);
   - the open drafts and agency overrides in the domains the sets touch (P-4);
   - the facts each builder sends, against its schema;
   - the ESS paths and the tests that drive them;
   - the acceptance classes of P-11.
6. **The server, read-only:** the running tag, and P-13's picture.

Commit: `chore(pol049): baseline, the domain reality table, modes and clients everywhere, bindings and their readers, open drafts and overrides, the server's templates and ledger`.

## P1 — The catalog, the capabilities, the guards, the page, the clients (plan §1, P-3)

- **The catalog:** `PolicyDomainCatalog` and its descriptors; `PolicyDomainOwners` and `PolicyWorkflowTriggerAllowlist` derived from it.
- **The validator:** `CatalogPolicyDomainCapabilities`, the interface extended, and `AllowAllPolicyDomainCapabilities` gone from every production path.
- **Run time and start-up:** the orchestrator's neutral decision, the internal endpoint's 400, the resolver's Off, and the start-up check.
- **The page:** the table from the catalog, and `CreateDraft` refused on the page and in the API.
- **The clients:** removed from the eight services; the wiring file updated.
- **The document:** `docs/POLICY_DOMAIN_REGISTRY.md` matches the catalog.
- **The script:** `scripts/pol049-validate.ps1`, as the plan says: the catalog, the page and the registry document agree, and no service registers the client without calling it (P-12).
- **Tests:**
  - `Pol049PolicyDomainCatalogTests` (with P-3's call-site rule, and the registry document check);
  - the derived allowlist is empty;
  - one validator test per refusal;
  - the start-up check;
  - `CreateDraft` refused on the page and in the API;
  - `ProductionWiringMatrixTests`.

Commit: `feat(pol049): one domain catalog drives the validator, the engine, the internal endpoint, the start-up check and the policy page; the client stays only where it is called`.

## P2 — The change sets, the migration, the fresh seed, the bindings (plan §2, §3, P-4, P-5)

- **The applier:** `RetireDomain`, and the quiet pass over an `OutOfScope` domain on a fresh database.
- **The sets and the migration:** the four sets, and `POL049_PolicyDomainRetirement` (`has-pending-model-changes` clean, no byte order mark).
- **The seed:** the fresh seed of the five domains, and the bindings, R-EST-SUBMIT's included.
- **Tests:**
  - `Pol049EffectiveRulesConformTests`, `Pol049FreshSeedTests`, and the added condition in `WorkflowDefinitionsHaveHandlersTests`;
  - `Pol049ScopeChangeSetsPostgresTests` (new versions, never an edit in place; `RetiredAt`; a second start changes nothing).

Commit: `feat(pol049): fifteen domains retired by a final version; asset rules bound to Assign; the orphan card rule and the placeholder leave rules retired; a fresh database seeds the five governed domains`.

## P3 — Facts, the leave channels, the establishment scope (plan §4, P-6)

- **The builders:** facts without `employeeId`, `isSelfApproval` sent on approval, the tenant from the entity, and `ForAdvanceRequest` gone.
- **Leave:** update and approval through the engine; ESS with the local validation, the engine and D-POL-14.
- **Establishment:** `actorIsBranchScoped` from `HrOrgScopeVisibility.IsBranchScoped(User)`.
- **Tests:** `Pol049FactsBuildersMatchSchemasTests`, `Pol049LeaveChannelsTests` (Shadow and Enforce, with a fake client), `Pol049EstablishmentOrgScopeFactTests`.

Commit: `feat(pol049): facts match their schemas; leave passes the engine on create, update, approve and ESS; establishment sends the actor's real scope`.

## P4 — Production modes, and the coordinator (plan §5, §6, P-7, P-8)

- **The modes:** the keys in the Production and Staging files and in the prod-like compose.
- **The coordinator:** the code comparison, and the cancel in one transaction.
- **Tests:**
  - `Pol049PolicyModeConsistencyTests`;
  - `Pol049CoordinatorWorkflowCodeTests`, `Pol049CoordinatorCancelPostgresTests`;
  - P-8's non-owner integration test, if that is the route.

Commit: `feat(pol049): the five governed domains run in Shadow in Production, Staging and prod-like; a policy may start only the workflow the service asked for`.

## P5 — Acceptance, then the independent review (plan Acceptance, P-9, P-10, P-11)

1. **The pieces of the acceptance run** (P-10):
   - the `pol49` overlay and the weak-offer seeding;
   - the boot-check script;
   - `ci-scope.json` and `ac-ownership.json`.
2. **Acceptance.**
   1. Start the stack (`acceptance-up.ps1 -Profile ProdLike`), and record the time to ready.
   2. Run `AcPol49EngineScopeTests` (AC-POL-49a..e and g) and the `pol49` group (AC-POL-49f and the boot check), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Record the latency of the Shadow calls in AC-POL-49b and AC-POL-49d (the plan's first risk).
   4. Then run P-11's list, `scripts/pol049-validate.ps1` and `scripts/acceptance-verify.ps1`. All required classes must Pass.
   5. `down -v`. Restore the committed evidence.
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a second source of domain truth left anywhere: a map, a list, a document, a page;
   - a path where an out-of-scope domain still evaluates, writes a ledger row, starts a workflow, or boots in Shadow or Enforce (`DefaultMode` included);
   - an effective rule outside its descriptor;
   - a seed path that publishes what the validator would refuse (`Create` without the container, the agency packs);
   - a change set that edits a published version or an applied set;
   - a leave channel that skips the engine or the local validation, or drops an ESS request silently;
   - the coordinator accepting another code, or leaving a cancelled instance visible or reusable;
   - a Shadow call that can block or change an outcome, or write its ledger row in the wrong tenant;
   - a client removed from a service that still calls it, or an address key removed that another client reads;
   - something PLAN-48's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(pol049): AC-POL-49a..g pass on the prod-like stack; Shadow ledger rows proved under forced row security; review fixes; CI scope and ownership updated`.

## P6 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(pol049): main merged, conflicts resolved, migrations on top`.

## P7 — The demo server (P-13, P-14)

Record every server change and its undo command **as you make it**.

1. Check P-14's condition.
2. Take the backup, then **verify** it. Take the read-only before-picture of P-14.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - start-up applies the migration and the four sets;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan49/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them.
   1. `__EFMigrationsHistory` holds `POL049_PolicyDomainRetirement`, and `PolicySeedApplications` holds the four sets.
   2. **The templates:**
      - the fifteen out-of-scope templates carry `RetiredAt` and a final published version with no rules;
      - the effective versions of the five governed templates hold none of the retired rules, and the asset rules carry `action=Assign`.
   3. The start-up inventory lists no rule of an effective version.
   4. **Modes:** the four services run their governed domains in Shadow, and no domain runs in Enforce.
   5. **Ledger rows:**
      - the PLAN-43..46 server checks drive recruitment, custody and cards;
      - add one leave action and one establishment action to a new `AC_POL_49_vps_…` check, with users the server checks already use;
      - every governed domain then has a Shadow ledger row, with the owner's `CallerClientId` for the three external domains;
      - record what the actions created, and the latency.
   6. `/PolicyTemplates` shows five governed domains in Shadow and fifteen out of scope.
   7. The eight services run without the client, and the wiring check is green.
   8. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   9. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..48 server checks.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-14 says, prove the demo works, and record it.

Commit: `test(pol049): demo server on the domain catalog (fifteen domains retired, five in Shadow with live ledger rows), PLAN-36..48 server checks still green`.

## P8 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/POLICY_DOMAIN_REGISTRY.md` (from the catalog, checked by the test);
   - `docs/policy-rules-inventory.csv`, with the «محكوم/محلي» column;
   - `docs/POLICY_ENGINE_UAT.md`;
   - `OPS_VPS_SINGLE_HOST.md`: Shadow on the server, the backup location, the change table, the rollback note of P-14;
   - the «الحالة» lines for G-POL-02, G-POL-04, G-POL-09 and G-POL-11 in `POL_001_GAP_STUDY.md`;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local CI, then the pre-push check, before any push** (P-2):
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD;
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

   Both must be green.
3. **`reports/plan49-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. **The inventories:** the domain reality table before and after (domains, call sites, effective rules, effects); modes and clients; bindings; open drafts and overrides.
   5. The guards and their own negative tests.
   6. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   7. **The cost of Shadow:** the latency measured in AC-POL-49b and AC-POL-49d, and on the server.
   8. **The server:** before and after (templates, effective versions, modes, ledger rows), changes and their undo, the backup location, the rollback note.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time against waiting for CI, and any time spent waiting for the owner;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 49`.
   12. **A readiness note:**
       - for PLAN-65: AC-09/AC-10 and the PROD-001 documents now sit on this catalog;
       - for wave 2 (G-POL-10, D-POL-13): what the Shadow ledger now records per domain, and what a Shadow report needs.
4. **Merge (P-15), by you:**
   1. Merge the feature branch into a new `release/plan-49` from `main` (`--no-ff`).
   2. Write `reports/plan49-pr.md` (short).
   3. `git push -u origin release/plan-49`
   4. `gh pr create --base main --head release/plan-49 --title "PLAN-49: what the policy engine governs, facts that match their schemas, and no dead or orphan rules" --body-file reports/plan49-pr.md`
   5. Wait for the checks in the session («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan49-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. Commit that note on a local branch `docs/plan-49-merge-note`, as the plans before did, so the next plan brings it in; **do not open a second PR for it.**

## Definition of done

- **One catalog** drives the validator, the engine, the internal endpoint, the start-up check, the page and `POLICY_DOMAIN_REGISTRY.md`. There is no second registry.
- **The five governed domains** run in Shadow in Production, Staging and prod-like. Each has a proved call site and live ledger rows, on the stack and on the server. Shadow writes its ledger row in the caller's tenant under forced row security, proved as a non-owner role.
- **The fifteen others** are retired by a final version and documented, and the client remains only where it is called.
- **No effective rule** lies outside its domain's capabilities: no unconsumed effect, no placeholder value, no orphan `TriggerWorkflow`. The start-up inventory lists no rule of an effective version.
- **Facts match their schemas.** Leave passes the engine on create, update, approve and ESS, and establishment sends the actor's real scope.
- **The coordinator** never accepts a workflow code other than the one the service asked for.
- **The tests:** AC-POL-49a..g = Pass (the PR run's artifact is the evidence of record), and the PLAN-25 and PLAN-36..48 suites are green.
- **The server** runs this code.
- **The documents** P8 lists are updated.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server (the retired domains, the Shadow ledger rows);
- the measured cost of Shadow, in one line;
- how long the plan took, how long it waited for CI, and any time it waited for the owner;
- the PR link;
- the path of the final report.
