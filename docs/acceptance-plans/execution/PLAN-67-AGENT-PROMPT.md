# Task: PLAN-67 — A readable Arabic interface, one content security policy per page, and a clean demo (UX-A)

The deliverables:
- **The browser blocks nothing on the demo:**
  - every page arrives with one content security policy (CSP);
  - no markup carries an inline event handler or a `style` attribute;
  - every confirmation shows before a destructive action, and every reviewer's comment reaches its record.
- **Everything a user reads is Arabic:**
  - statuses and labels in Arabic;
  - people and records by name, not by id;
  - times in Damascus time, in one format.
- **Policy rules read as Arabic sentences,** the simulation takes a form, and JSON sits behind «عرض تقني» for specialists.
- **The public careers portal** lives at `/careers`, fully styled, with no test residue.
- **A guard stops all of this from coming back,** and screenshots of the key pages go into the evidence.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P10** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**Why this plan exists:** on 2026-11-13 the owner sent two screenshots from the demo:
- a policy template page full of English labels, raw JSON, user ids and UTC times;
- the public careers portal, unstyled, opened at `/ui/hr-core/`.

The study `UX_004_INTERFACE_GAP_STUDY.md` traced both to the code and found more behind them.

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks. **PLAN-52 must be merged first** (Preconditions).

**Complete execution** (owner, 2026-11-12, standing since PLAN-51): the owner wants every plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns elsewhere stays out: its «Out of scope».
- Where the plan speaks in general, this prompt makes it concrete, and all of it is in scope:
  - **every** view of every service and of the gateway, not only the pages in the screenshots: the study counted 652;
  - every list the guard keeps ends empty; the only exceptions are the technical ids the guard names in its own code, each with its reason;
  - whatever P0 proves to be the cause of the blocked styles is fixed here, even if it is not the cause the study expects.

**Standing owner instruction on merges** (PLAN-38 to PLAN-52 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-19).
- **Never ask the owner whether to merge:** his instruction is the permission. Asking cost PLAN-51 2 h 09 min.

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan67-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan67-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan67-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep, a blocked port, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in the Claude desktop app, at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files, screenshots) under `artifacts/plan67/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.
- Many views change in this plan. Change them by pattern, service by service, with a script or a careful search, and let the guard count what is left.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan67/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, `ci-local.ps1` and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A call that returns at the limit is not a refusal: run it again. A background `gh run watch <id> --exit-status` is fine too, if this session's background commands wake it when they end.
- **Then act at once:**
  - every check green → merge (P-19), with no question to the owner;
  - a red check → read its log as soon as it fails, without waiting for the other checks; fix it on the release branch, push that branch only, and wait again the same way.
- **Only if this app truly cannot keep you in the turn.** PLAN-51 ended its turn after opening the PR, believing the app would not let it poll, and then asked the owner whether to merge: the merge waited 2 h 09 min. In that case:
  1. End the turn with `WAITING-CI:` (see below), and ask nothing.
  2. On the owner's next message, whatever it says, check **once** with `gh pr checks <n>`.
  3. Every check green → merge at once. A check still running → wait for it in that turn, as above.
  4. Record the path you took, with the times. Do not use GitHub's auto-merge.
- **A run that never starts:** if the PR's run stays queued with no jobs for 15 minutes, look at GitHub's status page. During an Actions incident such a run can be neither cancelled nor rerun. Close and reopen the PR to start a new run on the same HEAD, as PLAN-50 did, and record it.
- **The app's controls:** if the owner turns on **Auto-fix** for the PR, its wake-ups are welcome, but they do not replace your waiting. Never use **Merge when ready**: it squashes, and you merge with `gh pr merge <n> --merge`.
- **`WAITING-CI:`** ends the turn with a line that starts with `WAITING-CI:` and names the PR, the run id, its URL and the exact next step.
- Never merge on a guess.

**Blocking decisions.** When a "Stop" condition is hit:
1. Write `BLOCKED:` with the question and options.
2. Commit what is complete.
3. End the turn. **Do not guess.**

---

## Role

You are a senior .NET engineer for web interfaces that must be correct, secure and readable:
- ASP.NET Core MVC and Razor: views, partials, tag helpers, HTML helpers;
- Content Security Policy Level 3 with nonces, and how a reverse proxy (YARP) carries response headers;
- browser checks with Playwright: CSP violation events, computed styles, screenshots;
- Arabic right-to-left interfaces: localization, time zones, display formats;
- architecture tests that scan source files, with ceiling lists that only shrink;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..52 did.

**Where things stand** when this prompt was written (2026-11-13):
- **PLAN-52 was running.** `main` was `e6a93705` (PLAN-51). When you start, `main` holds PLAN-52 (Preconditions):
  - read its final report and its merge note (`docs/plan-52-merge-note`);
  - the demo server runs PLAN-52's tag (`/opt/hrm/current-tag`): that tag is your rollback target;
  - PLAN-52 adds a demo user, `e2e-branch-manager`, and scope attributes on controllers; it does not touch the views this plan changes, except the Diwan and inspection forms and some pick-lists.
- **This plan's study and plan** are staged, untracked, under `docs/acceptance-plans/execution/PLAN-67-staged/`, so that PLAN-52's clean-tree check did not trip on them. P-1 moves them to their places.
- **PLAN-51 took 19 h 51 min,** of which about 6 h 38 min were active. Its stops:
  - SSH to the server was blocked for 3 h 20 min: the owner's network blocks outbound port 22, and only ProtonVPN let it through;
  - the session stood still for 7 h 44 min after the pre-push check, until the owner pressed «Try again»;
  - it ended its turn after opening the PR, and the merge waited 2 h 09 min for the owner's word.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks and took 59 min in PLAN-51: `CI` 41 min (the longest required check), `meta` 35 min, `prodlike-final` 31 min, `lab-main` 30 min, `prodlike` 29 min. One acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the required checks on `main` are `CI`, `CodeQL`, `Gitleaks`, `E2E` and `Acceptance`. The last step of `Acceptance` («Every stage job succeeded») fails when the image build or any stage failed;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs. The `prodlike` job runs the stages `journey`, `main`, `isolated` and `s2s42`, and `acceptance-job.ps1` runs them with `RUN_PLAYWRIGHT_E2E=true`, so the prod-like journey's browser is available there;
  - **the build and test steps live in `scripts/ci/steps.json`,** which CI and `ci-local.ps1` both read; `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-51:**
  - `src/Tests/HRManagement.ArchitectureTests` holds `WebAssemblyCatalog`, `ActionCatalog`, `CeilingList` and `GuardSelfTests`;
  - a `CeilingList` row is `Key | reason`, and the reason must start with an owner listed in `CeilingList.Owners`. PLAN-52 adds `IAM-H`;
  - every write names its policy, and a new action must too;
  - the gateway's own pages answer a refusal with a 302 to an `/Account/AccessDenied` page that does not exist (a defect for later, not this plan's).
- **From PLAN-50:** «interactive staff» is defined once, by `GatewayApprovalReachAuthorization` and `Authorization:NonStaffRoles` (`hr-employee`, `hr-exam-marker`, `applicant`); Keycloak's own roles never count. PLAN-51's `HrCore.Lookup` uses the same definition (D-51-3).
- **From PLAN-47:** a published policy version is immutable; database triggers refuse an update. Only how it is shown may change.
- **The code today** (read on `e6a93705`; the study cites file and line, and PLAN-52 may have moved a few lines: find the code by its content):
  - **The CSP:**
    - `UseHrSecurityHeaders` builds `script-src 'self' 'nonce-…'; style-src 'self' 'nonce-…'`, with a new nonce per request in `HttpContext.Items` (`HrSecurityHeadersExtensions.cs:49-83`). `Security:CspMode` defaults to `ReportOnly` (`:15`);
    - `appsettings.Production.json` sets `Enforce` in all fifteen apps (fourteen services and the gateway). The demo runs Production;
    - `CspNonceTagHelper` puts the request's nonce on every `<script>` and `<style>` in the views of each app;
    - **the gateway and every service** call `UseHrProductionMiddleware`, which calls `UseHrSecurityHeaders` (`ProductionHostExtensions.cs:85-103`). In the gateway this runs before `MapReverseProxy` (`Gateway/Program.cs:190, 253`), so the gateway writes its own CSP before it forwards the request. YARP 2.3.0 adds the destination's headers to those already on the response; it does not replace them. **The study expects two policies on every proxied page** (needs P0's proof);
    - neither `docker-compose.acceptance-prodlike.yml` nor `docker-compose.ci-e2e.yml` sets `Security__CspMode`, so acceptance and E2E run `ReportOnly`. No test mentions `Content-Security-Policy`.
  - **What CSP blocks even with one policy** (a nonce never covers these):
    - **12 inline handlers, all in HR.Core:**
      - ten `confirm()` calls: `RecruitmentRequisitions/Details.cshtml:84, 92, 106, 180`, `FundedPositions/Details.cshtml:80, 88`, `EstablishmentChanges/Details.cshtml:86`, `JobCatalog/Details.cshtml:22`, `ReferenceCatalog/Details.cshtml:88`, and the step rejection in `Shared/_WorkflowActionPanel.cshtml:57`;
      - two `onclick` in `Approvals/Review.cshtml:95-105` that copy the reviewer's comments into the hidden `reviewComments` of the approve and reject forms. `ApprovalsController.Reject` accepts empty comments;
    - **46 `style="…"` attributes** in ten projects: progress bars (`width:@(pct)%`), the notification container (`_LiveNotifications.cshtml:2`, `position:fixed…`), eight `display:inline` forms, a `white-space: pre-line`.
  - **What two policies would block too:** 16 inline `<script>` blocks (among them the hire mode switch in `RecruitmentApplications/Hire.cshtml:157-162` and the payroll and contracts dashboard charts) and four `<style>` blocks: `_PublicLayout.cshtml:10-129` (all of the portal's own styles) and `_OrganizationBranding.cshtml:9-12` (the agency colour on every page).
  - **The design system that exists:**
    - `_HrSharedLayout`, `syrian-theme.css` and 27 shared partials (`_HrEmptyState`, `_HrDataTable`, `_HrConfirmModal`, `_EmployeeDisplayName`…);
    - `data-hr-confirm` (UX-003, `shared-ui.js:372`);
    - `HrEnumDisplay` and `HrStoredEnumDisplay` (`HrDisplayHtmlExtensions.cs:9-15`), used in 302 places;
    - `display-names.js` hydrates `data-hr-employee-id` and `data-hr-org-unit-id` through `/api/lookup/*/by-ids` (`:150-160`);
    - `ICalendarService` and `DamascusCalendarService` (L10N-001), used by no view and no controller.
  - **Arabic gaps:**
    - 27 English labels, 21 of them in six policy pages;
    - about 38 raw prints of a status or type, among them the requisition badge (`RecruitmentRequisitions/Details.cshtml:216`, prints `PendingApproval`), `FundedPositions/Index.cshtml:73`, `EstablishmentChanges/Index.cshtml:75-76`, and in the policy pages `@Model.Scope`, `@v.Status`, `@publication.Kind`, `@row.Mode`;
    - the policy enums carry no `[Display]` (`PolicyEntities.cs:7-18, 78-81`), and `PolicyDomainDescriptor` has no Arabic name;
    - internal codes (`D-POL-1`, `R-ETL`, `POL-049-SCOPE-RETIRE`, `(RequireApproval)`) sit inside user text.
  - **Ids:**
    - about 125 places print an id in 14 projects, the policy page's maker and checker among them (`PolicyTemplates/Details.cshtml:88-89`);
    - nothing turns a user id (Keycloak `sub`) into a name. Auth.Directory cannot either on the demo: its demo seed creates `emp001`… without `KeycloakUserId` (`DemoAuthSeeder.cs:82-86`), and the `e2e-*` users are not there;
    - the gateway's audit stores `UserSub`, `UserName` and `TenantId` for every request (`GatewayAuditEntry.cs:10-11, 22`), in `GatewayAuditDbContext`.
  - **Time:** 97 places show a time with hours, none converted from UTC. Five date formats: `yyyy/MM/dd` 123, `yyyy-MM-dd` 112, `yyyy/MM/dd HH:mm` 61, `yyyy-MM-dd HH:mm` 42, `dd-MM-yyyy` in the portal. `PolicyTemplates/EditRules.cshtml:106-107` asks for a UTC input.
  - **Policy JSON:**
    - shown in `PolicyTemplates/Details.cshtml:112-113` and `EditRules.cshtml:65-66`, written in `EditRules.cshtml:46-47, 88-89`, simulated in `Simulate.cshtml:15-16`;
    - every facts schema field has `DisplayNameAr` and a `Type` (`PolicyFactsSchemaModels.cs:3-9`, seeds in `PolicyEngineDomainSeeds.cs:120-121`);
    - eight operators (`PolicyRuleJson.cs:20-23`), eleven effects (`PolicyEffect.cs`);
    - the right pattern exists in `EstablishmentChanges/Details.cshtml:44-51`: a readable summary, and the raw payload folded under «حمولة JSON (تشخيص)».
  - **The portal:** `PublicCareersController` has `[Route("")]` and `[HttpGet("")]` (`:23-24, 36-37`), so it owns HR.Core's root and hides the default `{controller=Dashboard}` route (`HR.Core Program.cs:376-378`). The gateway's public route `careers-public` maps `/careers/{**catch-all}` to it (`Gateway appsettings.json:380-384`).
  - **Demo residue:** eight server check classes leave records marked «PLAN-xx server check»:
    - `AcOps38VpsDemoDefectChecks`, `AcWf43`, `AcWf44`, `AcWf45`, `AcWf46`, `AcPol47`, `AcPol49` and `AcIam50` (`…VpsChecks`);
    - the approved requisitions of 43, 44 and 50 show on the public portal;
    - `AcPol49VpsChecks`' third check reads what the checks of PLAN-43..46 wrote since the deploy.
  - **Empty states:** 27 of the 211 views that loop over a table show no empty message, 18 of them in HR.Core.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). Lift `FORCE` inside the migration, as `WF044` does. This plan's one migration (the names registry) is in the gateway's own database; check whether row security applies there.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role.
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. Make sure CI's Testcontainers filter picks up your new classes.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`. **The CSP mode was such a value** — this plan mirrors it.
  - **Personas and users:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start;
    - a new persona also changes `DATA-AUTH` in `scripts/acceptance-verify.ps1`;
    - a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh`.
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check, and in PLAN-50 it failed ci-local twice on whitespace, about 17 minutes each time.
  - **Acceptance tests:**
    - a test posts the URL and the token the page renders (PLAN-44);
    - policy API writes need the antiforgery token (`AcceptanceHarness.CreatePolicyLifecycleClientAsync`, PLAN-47).
  - **The machine:**
    - after the host sleeps, the Docker VM's clock lags, and time-based tests fail until it catches up (PLAN-50);
    - **SSH and the VPN** (PLAN-48, PLAN-51): the owner's network may block outbound port 22, and only ProtonVPN lets it through; but with the VPN on, Testcontainers fail with "Failed to connect to 192.168.65.254". So order the work (P-2).
  - **The session may stand still** (PLAN-51: 7 h 44 min after the pre-push check, until «Try again»). Keep the progress file's next step exact, and go from the pre-push check straight to the push and the PR, in the same turn.
  - **The server:**
    - run the server build detached (PLAN-44), and the server suites with nothing heavy beside them (PLAN-45);
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan67/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49);
    - a user without HR.Core signs in through another page; a refusal of the gateway's own pages is a 302; the gateway audits only what it forwards (PLAN-51).
  - **Before the push:**
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql;
    - run the full Testcontainers step on the host with CI's filter, when `ci-local.ps1` runs with `-SkipTestcontainers`;
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging`;
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it.
  - **The architecture project's first run after a build** takes about 2 min 25 s, outside the tests (PLAN-51).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools. Many views change here: prefer the Edit tool per file, or a script that reads and writes UTF-8 without a byte order mark.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. **The specification:**
   - `docs/acceptance-plans/PLAN-67-ux-readable-arabic-interface.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
   - `docs/UX_004_INTERFACE_GAP_STUDY.md` — the study behind it, with every reference.
   - Both are staged until P-1 moves them.
2. **These reports:**
   - `reports/plan52-final-report.md` and its merge note;
   - `reports/plan51-final-report.md` §3 and §10;
   - the parts of `reports/plan50-final-report.md` that define interactive staff;
   - the immutability parts of `reports/plan47-final-report.md`.
3. **These documents:** `docs/ADR-UX-001-xss-csp-sri.md`, `docs/UX_001_VERIFICATION.md`, `docs/UX_003_CONFIRM_DIALOGS.md`, `docs/L10N_001_LOCALIZATION.md`, `docs/L10N_003_ARABIC_UI.md`, `docs/GATEWAY_RUNBOOK.md`, and G-POL-14 in `docs/POL_001_GAP_STUDY.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`,** on `main` or on `docs/plan-52-merge-note`, and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-52: the merge of `release/plan-52` is in `git log origin/main --merges --oneline -5`.
3. **The working tree is clean,** except for these, which may be present:
   - the owner's local `global.json` edit;
   - `tmp-vendors.html`;
   - git-ignored `artifacts/`;
   - untracked prompt files under `docs/acceptance-plans/execution/`, this plan's staged folder included.

   Restore any test-rewritten `testdata/*/out/*.json` with `git checkout -- testdata/*/out`, and any locally rewritten evidence with `git checkout -- acceptance-evidence; git clean -fd acceptance-evidence`.
4. **`ssh -o BatchMode=yes hrm-vps "whoami"` prints `hrm`.** The ED25519 host key is still `SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto`; never bypass host-key checking.
   - **If port 22 is blocked** from the owner's network, this precondition does not stop P0. Record it, take the server's read-only picture at the start of P9, and go on with the local phases (P-2).
5. **`gh auth status`** shows `omardefdev` with the `repo` and `workflow` scopes.

## Decisions — already taken (no owner questions)

The owner asked for this plan on 2026-11-13, after the study's findings. The plan's defaults apply as written. If the owner changed one in a message before P0 starts, his message wins; record it.

- **D-67-1, who owns a response's CSP:** the service that renders the page. The gateway adds its headers to its own pages, and to a response that arrives without them.
- **D-67-2, time zone and format:**
  - display in `Asia/Damascus`, from `Localization:TimeZone`; storage stays UTC;
  - dates as `yyyy/MM/dd`, times as `yyyy/MM/dd HH:mm`, with the hint «بتوقيت دمشق»;
  - inputs are local and converted on save.
- **D-67-3, user names:**
  - a registry in the gateway, filled at each sign-in from the token's claims (the `sub`, the display name, the agency);
  - answered only for the caller's agency, except to `platform-admin`;
  - «النظام» for an automatic actor, «مستخدم غير معروف» with the id in a tooltip otherwise.
- **D-67-4, who sees the rules' JSON:** policy authors, policy checkers and the admin, behind «عرض تقني».
- **D-67-5, the portal's address:** `/careers` alone; `/ui/hr-core/` opens the dashboard.
- **D-67-6, residue on the demo:** the public postings of marked requisitions are closed through the application's own action, with no deletion. A clean reset of the demo is a separate, later decision.
- **D-67-7, abbreviations:** Arabic first, the abbreviation in brackets: «رمز الصنف (SKU)».

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-67-ux-readable-arabic-interface` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-52's merge note from `docs/plan-52-merge-note`;<br>&nbsp;&nbsp;– move the staged files: `PLAN-67-staged/UX_004_INTERFACE_GAP_STUDY.md` to `docs/`, and `PLAN-67-staged/PLAN-67-ux-readable-arabic-interface.md` to `docs/acceptance-plans/`. Remove the empty staging folder, and commit them with this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine: awake, Docker, the disk, the ports, the VPN.**<br>• **Keep the machine awake for the session, changing no setting.** At P0, start a hidden background PowerShell process that holds `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)` and sleeps in a loop. Record its PID, and stop it at the end of the session. It does not stop a Windows Update restart; record one if it happens.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run, check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*`, Testcontainers, and untagged images of this repository's own builds. Never prune volumes or images of other projects.<br>• **The disk:** before every image build and every stack start, read the free space of the drive that holds Docker's data disk.<br>&nbsp;&nbsp;– Below 15 GB, remove this repository's leftovers first.<br>&nbsp;&nbsp;– If it is still below 15 GB, moving Docker's disk is a setting of the owner: `BLOCKED:`.<br>• **One heavy job at a time:** never an image build beside a host test run.<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** compare the stack's host ports with `netsh interface ipv4 show excludedportrange protocol=tcp`. If a port the tests read is reserved, freeing it is a system change for the owner: `BLOCKED:` with the range.<br>• **The VPN and SSH** (Lessons): Testcontainers need the VPN off, and SSH may need it on.<br>&nbsp;&nbsp;– Run the full Testcontainers step on the host **before** P9, on the code the server will get.<br>&nbsp;&nbsp;– Check SSH again just before P9. If port 22 is blocked then, `BLOCKED:` with one line: the owner turns ProtonVPN on, and you continue on his next message.<br>&nbsp;&nbsp;– When `ci-local.ps1` runs with the VPN on, use `-SkipTestcontainers`, and name the run of the full step it relies on. |
| P-3 | **The baseline evidence (plan 0.3–0.6).**<br>• **0.3, the cause, on the demo:**<br>&nbsp;&nbsp;– with `curl.exe -sI`, the CSP headers of `/careers`, of `/careers/track` and of a gateway page;<br>&nbsp;&nbsp;– with Playwright, signed in as `hradmin`, the response headers and the `securitypolicyviolation` events of ten key pages (P-12);<br>&nbsp;&nbsp;– count the policies in each response (two headers, or one header whose value holds two policies), and record each violation: page, directive, what was blocked.<br>&nbsp;&nbsp;The result decides P-4's first bullet. If the cause is not two policies, find the real one and record it.<br>• **0.4, the inventory:** generated, never typed, into `artifacts/plan67/ui-inventory.*`. Per view: English labels, raw status prints, printed ids, dates and their formats, JSON prints, inline handlers, `style` attributes, inline blocks, tables without an empty message. Compare its counts with the study's, and record the differences.<br>• **0.5, «before» screenshots:**<br>&nbsp;&nbsp;– of the key pages (P-12) at 1366×768 and 390×844;<br>&nbsp;&nbsp;– on your local prod-like stack started with `Security__CspMode=Enforce` (a local override, before any code change), and on the demo.<br>• **0.6, the demo's residue, read-only:** the records marked «server check» per type, and which of them the public portal shows. |
| P-4 | **One CSP per response, and nothing inline in the markup (plan §1).**<br>• **The gateway's headers:** written when the response starts, and only if the response does not carry them already (D-67-1). A proxied page then keeps its service's policy and nonce; a gateway page keeps the gateway's. The same rule holds for `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`. Prove it with gateway integration tests: a destination that sends a CSP gives one policy; a gateway page gives the gateway's, whose nonce matches the page's.<br>• **The 12 inline handlers:**<br>&nbsp;&nbsp;– the ten `confirm()` calls move to `data-hr-confirm` (UX-003), with the same Arabic text;<br>&nbsp;&nbsp;– the reviewer's comments reach the approve and the reject forms without inline code: one field the two forms share, or a script in a static file. Prove the comments reach the record.<br>• **The 46 `style` attributes:**<br>&nbsp;&nbsp;– they become CSS classes in `syrian-theme.css` or the service's stylesheet;<br>&nbsp;&nbsp;– computed widths (progress bars) use `<progress>`, or a `data-*` value that a script in a static file applies; CSSOM is not blocked.<br>• **Inline `<script>` and `<style>` blocks** may stay with their nonce once a page carries one policy; moving one to a file is fine.<br>• **The test stacks:** `Security__CspMode=Enforce` in `docker-compose.acceptance-prodlike.yml` and `docker-compose.ci-e2e.yml`, as in Production.<br>• **Never loosen the policy:** no `unsafe-inline`, `unsafe-hashes`, `unsafe-eval`, wildcard or external source, and no `ReportOnly` in a Production file. |
| P-5 | **Arabic vocabulary (plan §2).**<br>• **Every printed enum** gets an Arabic `[Display(Name = "…")]`, the policy enums first, and is printed through `HrEnumDisplay` or `HrStoredEnumDisplay`. A string status stored in English (such as an inspection's `"planned"`) gets the stored-enum helper or an Arabic map.<br>• **`PolicyDomainDescriptor`** gets `NameAr` for every domain, shown instead of the key; the key stays as a small secondary text.<br>• **The six policy pages:**<br>&nbsp;&nbsp;– Arabic labels («المجال»، «النوع»، «النطاق»، «القاعدة»، «الأولوية»، «الشرط»، «الأثر»، «تقرير الظل»…);<br>&nbsp;&nbsp;– internal codes (`D-POL-1`, `R-…`, evidence references) shown as secondary text, never inside the user's sentence.<br>• **Abbreviations:** D-67-7.<br>• No stored value changes. |
| P-6 | **Names instead of ids (plan §3).**<br>• **The registry:** a table in `GatewayAuditDbContext`, with its migration. At sign-in, or on the first authenticated request of the hour, the gateway upserts the user's `sub`, his display name from the token (`name`, else `preferred_username`) and his `tenant_id`. Check whether the gateway's database has row security, and follow its rules.<br>• **The lookup:** `POST api/user-names/by-ids` on the gateway, under a new named policy for interactive staff with PLAN-50's definition (never a second one):<br>&nbsp;&nbsp;– at most 50 ids;<br>&nbsp;&nbsp;– names of the caller's agency only, except for `platform-admin`;<br>&nbsp;&nbsp;– the name only: no e-mail, no roles;<br>&nbsp;&nbsp;– no service account, and no new scope.<br>• **The page:**<br>&nbsp;&nbsp;– `display-names.js` learns `[data-hr-user-id]`, with its own base (the gateway);<br>&nbsp;&nbsp;– a `_UserDisplayName` partial shows the name, «النظام» for an automatic actor (`system`, a seed), or «مستخدم غير معروف» with the id in a tooltip.<br>• **Linked records:** each printed foreign key becomes the record's name and link, through `data-hr-employee-id` and `data-hr-org-unit-id`, or a server-side include.<br>• **Technical ids stay,** each named in the guard with its reason (P-12): the correlation id on the audit page, the Keycloak id on the user admin page, the national number, hashes.<br>• **The report states,** on the demo after the server checks, how many users the registry knows and how many printed people remain «غير معروف». |
| P-7 | **Time in Damascus, one format (plan §4).**<br>• **`HrDate(…)` and `HrDateTime(…)`** in `HRManagement.Web.Shared/Html`, over `ICalendarService`, with the zone from `Localization:TimeZone` (default `Asia/Damascus`) and the hint «بتوقيت دمشق» (D-67-2).<br>• **Every date or time a view formats** goes through them. A `DateOnly` is formatted, never converted. `<input type="datetime-local">` keeps its ISO value; its meaning becomes local time.<br>• **Inputs:**<br>&nbsp;&nbsp;– the policy effective-from in `EditRules`, and every other `datetime-local` the inventory finds, is entered in local time and converted to UTC on save;<br>&nbsp;&nbsp;– PLAN-47's tests of effective dates stay green, with the conversion applied.<br>• **Tests:** midnight, the end of a month, the end of a year, and a stored value read back. |
| P-8 | **Readable policy rules, and the simulation form (plan §5).**<br>• **`PolicyRuleReadableRenderer`** in `HRManagement.SharedKernel/Policy` turns `WhenJson`, `ThenJson` and the domain's facts schema into one Arabic sentence:<br>&nbsp;&nbsp;– `all`, `any` and `not`;<br>&nbsp;&nbsp;– the eight operators;<br>&nbsp;&nbsp;– values by type;<br>&nbsp;&nbsp;– the eleven effects with their parameters.<br>&nbsp;&nbsp;It never throws: an unknown node renders «شرط غير معروف — انظر العرض التقني», and a field missing from the schema renders its key.<br>• **The pages:**<br>&nbsp;&nbsp;– the rule table shows the sentence, and the JSON folds under «عرض تقني» for D-67-4's roles only, as `EstablishmentChanges/Details.cshtml:44-51` does;<br>&nbsp;&nbsp;– `EditRules` previews the sentence beside its two JSON fields, which stay until POL-G;<br>&nbsp;&nbsp;– a version without rules shows an empty message.<br>• **`Simulate`:** a form generated from the facts schema (the field's type picks the input), with the JSON input kept as an advanced tab. The simulation service does not change.<br>• **Proof:**<br>&nbsp;&nbsp;– a parity test over every seeded and published rule: the sentence names each field, operator, value and effect;<br>&nbsp;&nbsp;– the form and the JSON give the same decision.<br>• No rule and no version changes (PLAN-47). |
| P-9 | **The public portal (plan §6).**<br>• `PublicCareersController` moves under `careers` inside HR.Core, and the gateway's `careers-public` route forwards `/careers/…` to it (D-67-5).<br>• `/ui/hr-core/` opens the dashboard; an anonymous visitor goes to sign-in.<br>• **Every public link keeps working:** `/careers`, `/careers/track`, competition results and their PDF, links in notifications, the E2E and acceptance URLs.<br>• **The portal's look** comes back with P-4, and is checked in the browser at both sizes: the card class is applied, the navbar has its colour, the button is a button.<br>• Its dates follow D-67-2. |
| P-10 | **Tables and empty messages (plan §7).**<br>• `_HrEmptyState` in the 27 tables the inventory lists.<br>• The policy tables get fixed column widths and wrapped text, after P-8. |
| P-11 | **Demo data (plan §8).**<br>• **The eight server check classes:**<br>&nbsp;&nbsp;– they write realistic Arabic titles and notes, and keep their marker in a field the public never sees (per entity: notes, justification, or a reference field);<br>&nbsp;&nbsp;– a requisition they create is not published on the public portal, unless publication is what the check proves; then the check closes it before it ends.<br>• **Each check keeps its meaning.** `AcPol49VpsChecks`' third check still finds what it reads.<br>• **The demo's existing residue** is closed in P9 (P-18), not here. |
| P-12 | **The guard and the screenshots (plan §9).**<br>• **`InterfaceTextGuardTests`** in `HRManagement.ArchitectureTests` scans every `.cshtml` in the repository, and flags:<br>&nbsp;&nbsp;1. an English label in `th`, `dt`, `label`, `button`, `legend`, `option` or `h1`–`h6`, outside the abbreviation list;<br>&nbsp;&nbsp;2. a raw print of a status or type property;<br>&nbsp;&nbsp;3. a printed `…Id`, `…By` or `UserId`, outside the technical exceptions;<br>&nbsp;&nbsp;4. a printed `…Json` outside the `_TechnicalJson` partial;<br>&nbsp;&nbsp;5. an `on…=` handler;<br>&nbsp;&nbsp;6. a `style=` attribute;<br>&nbsp;&nbsp;7. a table loop without an empty message;<br>&nbsp;&nbsp;8. a date or time formatted outside `HrDate`/`HrDateTime`.<br>• **Its lists** use `CeilingList`, in `tests/architecture/ui-text-allowlist/<service>.txt`:<br>&nbsp;&nbsp;– seeded in P2 from inventory 0.4, each row signed `PLAN-67:`;<br>&nbsp;&nbsp;– emptied by P3–P6; at the end `PLAN-67` leaves `CeilingList.Owners`;<br>&nbsp;&nbsp;– the technical exceptions are listed in the test's code, each with its reason, never as rows.<br>• **Each rule fails on a planted defect** (`GuardSelfTests`).<br>• **The key pages:**<br>&nbsp;&nbsp;– the portal (`/careers`, a posting's details, `/careers/track`);<br>&nbsp;&nbsp;– the gateway's home page, and HR.Core's dashboard;<br>&nbsp;&nbsp;– the employee list and an employee's details;<br>&nbsp;&nbsp;– the work inbox, and the approval review page;<br>&nbsp;&nbsp;– a requisition's details, and the hire page;<br>&nbsp;&nbsp;– the four policy pages (list, details, rules, simulation);<br>&nbsp;&nbsp;– an establishment change's details, and a funded position's details;<br>&nbsp;&nbsp;– HR.Contracts' hire wizard, the payroll dashboard, an asset's details, and the transfer list.<br>• **The screenshots:** each key page at 1366×768 and 390×844, deviceScaleFactor 1. They go to the acceptance evidence (the PR run's artifact) and to `artifacts/plan67/`; **commit no image.** |
| P-13 | **Existing tests that the new rules change.**<br>• Only these may change, never weakened:<br>&nbsp;&nbsp;– a test that reads an English label, a raw status, an id or a UTC time, which now reads the Arabic label, the name or the local time, with the same meaning;<br>&nbsp;&nbsp;– a test that reaches the portal at HR.Core's root;<br>&nbsp;&nbsp;– a server check class that P-11 changes.<br>• **Stay green unchanged:** AC-HCM-25, AC-IAM-50, AC-IAM-51, AC-IAM-52, AC-WF-43..46, AC-POL-47, AC-POL-49, both recruitment journeys, and `Hcm025PanelServerParityTests`. |
| P-14 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• **`AcUx67ReadableInterfaceTests`** (AC-UX-67a..f): `prodLike[]`, `required: true`, group `main`, in the `prodlike` job, where the journey's browser already runs (check how the journey gets it, and do the same); `[RequiredAcceptance]` and `EvidenceRecorder("AC-UX-67")`. It must not stop or recreate containers.<br>• **The browser checks** follow the prod-like journey's pattern:<br>&nbsp;&nbsp;– a listener for `securitypolicyviolation` registered before each navigation;<br>&nbsp;&nbsp;– the CSP response headers counted;<br>&nbsp;&nbsp;– computed styles read for the portal.<br>• **AC-UX-67a's negative** proves the check can fail. Build the bad page in the browser itself: `SetContentAsync` with a `<meta http-equiv="Content-Security-Policy">` and an inline handler, then trigger it. Never add a test page or a test endpoint to a service.<br>• `ac-ownership.json`: `AC-UX-67` (owner PLAN-67, `provesClaim: true`).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-UX-67/`. Copy what the report needs into `artifacts/plan67/`, then restore the committed evidence. |
| P-15 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.** Because the prod-like stack now enforces CSP, the list is wide:<br>• **prod-like:** `AcUx67ReadableInterfaceTests`, the prod-like journey, `AcHcm25ProductionApprovalTests`, `AcIam50ApproverReachDelegationTests`, `AcIam51ExplicitPolicyTests`, `AcIam52BranchScopeTests`, `AcWf43DefinitionGovernanceTests`, `AcTen48NationalScopeTests`, `AcPol49EngineScopeTests`, and the `isolated` group (`AcInt37`, `AcMsg40`, `AcWf44`, `AcWf45`, `AcWf46`, `AcPol47`, `AcOps39`);<br>• **lab:** `AcHcm21RequisitionApprovalUiTests`, `AcHcm22RecruitmentWorkflowsUiTests`, `AcHcm28CommitteeAppealTests`, `AcHcm29HireContractTests`, `AcHcm31CompetitionTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcWf23WorkInboxUiTests`, `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests`, the lab `isolated` group (`AcHcm26`, `AcHcm27`), and the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests` (the gateway's new migration runs there);<br>• **E2E:** the E2E suite on its own stack with `Enforce`, as the `E2E` check runs it;<br>• and every class that reads a page whose text this plan changes.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-16 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`.<br>• **`scripts/ux067-validate.ps1`** holds no Arabic literal and has a `-SelfTest` with planted defects, as `iam051-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no `unsafe-inline`, `unsafe-hashes`, `unsafe-eval` or wildcard in the CSP builder;<br>&nbsp;&nbsp;– `Enforce` in every Production file, in the prod-like compose file and in the E2E compose file;<br>&nbsp;&nbsp;– no `on…=` handler and no `style=` attribute in any view;<br>&nbsp;&nbsp;– the guard's lists are empty;<br>&nbsp;&nbsp;– the gateway migration carries no byte order mark.<br>• A `-Headers <url>` mode counts the CSP policies of a URL with `curl.exe`, for P0 and P9.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`. |
| P-17 | **The demo server facts this plan must respect.**<br>• It runs Production in demo mode, with CSP `Enforce`.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– the portal styled at `/careers`, with no test posting;<br>&nbsp;&nbsp;– Arabic everywhere, names instead of ids, and times three hours later than today's display (Damascus);<br>&nbsp;&nbsp;– confirmations before destructive actions, and the reviewer's comments kept;<br>&nbsp;&nbsp;– the policy rules as sentences.<br>• **Names on the demo** appear for every user who signs in after the deploy. The server checks sign in as the `e2e-*` users, so they become known when the suites run; record who stays «غير معروف».<br>• **P0 lists, read-only:** P-3's 0.3 and 0.6, and the running tag. |
| P-18 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan67-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-17's picture.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`. The gateway's migration runs at start-up.<br>• **The residue (D-67-6):** as `hradmin`, through the application's own action, close the public posting of each requisition marked «server check» that the portal shows.<br>&nbsp;&nbsp;– Record each id, and its undo: the reopen action, or, if the application has none, the backup.<br>&nbsp;&nbsp;– Delete nothing.<br>• **Rollback safety:** find out what PLAN-52's tag does with this plan's data: the names registry is a new table it never reads. Record it in `OPS_VPS_SINGLE_HOST.md`.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-52's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-19 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P10, after local CI and the pre-push check are green, in the same turn as the pre-push check (Lessons).<br>2. `gh pr create`, then wait («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-20 | **No regression of PLAN-25, PLAN-36..52 and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`, `AcIam51`, `AcIam52`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, `AcHcm29`, and both recruitment journeys;<br>• the architecture tests of PLAN-51 and PLAN-52, `GatewayApproverReachTests`, `GatewayPathAuthorizationMiddlewareTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`, `WorkflowDefinitionsHaveHandlersTests`, `Qa066*`, and the policy tests of PLAN-47 and PLAN-49;<br>• `scripts/iam052-validate.ps1`, `scripts/iam051-validate.ps1`, `scripts/iam050-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/pol049-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **POL-G (wave 3; the study proposes moving it earlier):** the structured rule editor, conflict detection, simulation on a draft.
- **WF-H (wave 3):** the workflow definitions screens.
- **A new visual identity or a redesign:** the identity stays, and its edges are fixed.
- **Other display options:** the Hijri calendar, an English interface, Arabic-Indic digits.
- **D-67-6's later decision:** a clean reset of the demo.
- **UX-002:** a full accessibility audit.
- **PLAN-51's defect:** the gateway's missing access-denied page (report it if you meet it, do not fix it).

**Never commit:**
- the owner's local `global.json` edit — check `git diff --cached --name-only` before every commit;
- `tmp-vendors.html`, `scripts/__pycache__/*`, `docs/thesis_diagrams/source/.$*`, or anything under `artifacts/`;
- another plan's untracked prompt file;
- test-rewritten `testdata/*/out/*.json`, or evidence from a local run;
- a screenshot or any other image;
- any secret, token or password (templates keep their placeholders, `__SVC_SECRET__` or `${…}`).

**Never:**
- push to `main` or force-push;
- merge with a pending or red check;
- touch the side PRs (#40, #42) or their branches;
- disable host-key checking;
- loosen a CSP (P-4), or set `ReportOnly` in a Production file;
- widen a policy, a scope or a role to make a page or a test work;
- change a published policy version, or delete demo data;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails** (port 22 at P0 excepted: Preconditions 4).
- **A page cannot work without loosening the CSP** (a library that needs `eval`, say): `BLOCKED:` with the page, the library and the options.
- **The names registry or its lookup would need a new scope, a new client, or data outside the gateway.**
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **The space for Docker's disk stays below 15 GB, or a host port the tests read is reserved** (P-2).
- **Port 22 is still blocked when P9 starts** (P-2): one line asking the owner to turn ProtonVPN on.
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-67-ux-readable-arabic-interface origin/main`. Bring in PLAN-52's merge note, move the staged study and plan, and commit them with this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan67-baseline.md`:
   - **0.2:** the build, and the architecture tests;
   - **0.3:** the cause on the demo (P-3), with its conclusion;
   - **0.4:** the inventory (P-3), and its differences from the study;
   - **0.5:** the «before» screenshots (P-3);
   - **0.6:** the demo's residue (P-3).
4. If port 22 is blocked, record it, and take 0.3 and 0.6 at the start of P9: then the cause is decided on the local Enforce stack first, and confirmed on the demo before the deploy.

Commit: `chore(ux067): baseline, the CSP cause on the demo, the interface inventory, before screenshots, the demo's residue`.

## P1 — One CSP per response, nothing inline (plan §1, P-4)

- **The headers:** the gateway's rule, with its integration tests.
- **The markup:** the 12 handlers and the 46 `style` attributes, service by service.
- **The test stacks:** `Enforce` in the prod-like and E2E compose files.
- **Tests:**
  - the gateway's header tests;
  - the reviewer's comments reach the record (integration);
  - a destructive form asks first (the shared confirm, in a browser test).

Commit: `fix(ux067): one content security policy per response; confirmations and reviewer comments without inline code; style attributes become classes; the prod-like and E2E stacks enforce the CSP`.

## P2 — The guard (plan §9, P-12)

- **`InterfaceTextGuardTests`,** its lists seeded from inventory 0.4, `CeilingList.Owners` gains `PLAN-67`.
- **The planted defects** in `GuardSelfTests`.

Commit: `test(ux067): a guard over every view for English labels, raw statuses, ids, JSON, inline code, empty tables and unformatted times, with lists that only shrink`.

## P3 — Arabic vocabulary and time (plan §2, §4, P-5, P-7)

- **Vocabulary:** the enums' Arabic names, the helpers, the domain names, the policy labels, the abbreviations.
- **Time:** `HrDate`/`HrDateTime`, every format replaced, the inputs converted.
- **Tests:** the time helper's boundaries; the effective-date tests of PLAN-47; the lists' rows for these kinds are gone.

Commit: `feat(ux067): Arabic statuses, labels and policy domains; every time in Damascus time and one format`.

## P4 — Names and empty tables (plan §3, §7, P-6, P-10)

- **Names:** the registry and its migration, the lookup and its policy, `display-names.js`, `_UserDisplayName`, the linked records, the technical exceptions.
- **Empty tables:** the 27.
- **Tests:**
  - the registry's upsert;
  - the lookup's policy: interactive staff, `hr-employee`, a service account, another agency, `platform-admin`;
  - the lookup at the gateway;
  - the lists' rows for these kinds are gone.

Commit: `feat(ux067): people and records by name, from a gateway registry of signed-in users; empty tables say so`.

## P5 — Readable policies (plan §5, P-8)

- **The renderer,** the sentence in the pages, «عرض تقني», the `EditRules` preview, the simulation form, the version history in Arabic.
- **Tests:** the parity test over every seeded and published rule, the form's parity with the JSON input, the JSON's visibility per role.

Commit: `feat(ux067): policy rules read as Arabic sentences, the simulation takes a form, and the JSON stays behind a technical view`.

## P6 — The public portal and the demo data (plan §6, §8, P-9, P-11)

- **The portal:** under `careers`, `/ui/hr-core/` to the dashboard, every public link checked.
- **The eight server check classes** changed (P-11).
- **The lists:** every `PLAN-67:` row is gone. `PLAN-67` leaves `CeilingList.Owners`.

Commit: `feat(ux067): the public portal lives at /careers alone; server checks leave no public residue; the guard holds no rows`.

## P7 — Acceptance, then the independent review (plan Acceptance, P-13, P-14, P-15)

1. **The pieces of the acceptance run** (P-14): the class, the test-only page, `ci-scope.json`, `ac-ownership.json`, `scripts/ux067-validate.ps1`.
2. **Acceptance.**
   1. Check the disk (P-2). Start the stack (`acceptance-up.ps1 -Profile ProdLike`, now with `Enforce`), and record the time to ready.
   2. Run `AcUx67ReadableInterfaceTests` (AC-UX-67a..f), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then run P-15's list, `scripts/ux067-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must pass.
   4. `down -v`. Restore the committed evidence.
3. **The full Testcontainers step on the host,** with CI's filter and the VPN off (P-2).
4. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan, the study and the diff, and hunts for:
   - a page that still carries two policies, or a policy loosened anywhere;
   - an inline handler, a `style` attribute, or inline code that does not carry the page's nonce;
   - a destructive action without its confirmation, or a form that loses typed text;
   - an English label, a raw status, an id, an unconverted time or a raw JSON left in any view, or a guard rule that cannot fail;
   - a user name answered to another agency, to a non-staff user or to a service;
   - a rule sentence that says something its JSON does not;
   - a public link that broke with the portal's move, or a server check that lost its meaning;
   - a part of the plan deferred, skipped or listed to save time;
   - something PLAN-52's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(ux067): AC-UX-67a..f pass on the prod-like stack under an enforced CSP; review fixes; CI scope and ownership updated`.

## P8 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(ux067): main merged, conflicts resolved`.

## P9 — The demo server (P-17, P-18)

Record every server change and its undo command **as you make it**.

1. Check SSH (P-2) and P-18's condition.
2. Take the backup, then **verify** it. Take the read-only picture (P-17), and P0's 0.3 and 0.6 if they waited.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`. If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident). Record the downtime and the time to all healthy.
4. **Close the residue's public postings** (P-18), each with its undo.
5. **Then check**, with evidence in `artifacts/plan67/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcUx67VpsChecks` class, and each one cleans up what it leaves open.
   1. **One policy, no violation:** every key page, signed in as `hradmin` (and the portal anonymous), carries one CSP and raises no `securitypolicyviolation`.
   2. **The portal:** `/careers` is styled (computed styles), shows no «server check» text, and keeps its links; `/ui/hr-core/` opens the dashboard for `hradmin`.
   3. **The policy page:** Arabic labels, the rules as sentences, the JSON only under «عرض تقني» for a policy author, times in Damascus time.
   4. **Confirmations and comments:** a destructive form asks first (cancel it: nothing changes); the reviewer's comments reach the record, on a record the check creates and removes.
   5. **The «after» screenshots** of the key pages at both sizes.
   6. **Everyone else is unchanged:** the PLAN-36..52 server checks pass, and `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   7. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..52 server checks. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
   8. **After the suites:** how many users the registry knows, how many printed people remain «غير معروف», and that the suites left no public residue.
6. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-18 says, prove the demo works, and record it.

Commit: `test(ux067): demo server with one CSP per page, a styled portal at /careers, Arabic pages and readable rules; PLAN-36..52 server checks still green`.

## P10 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/UX_001_VERIFICATION.md`: `Enforce` everywhere it runs, one policy per response, and the CSP check;
   - `docs/UX_003_CONFIRM_DIALOGS.md`: no inline `confirm()` left;
   - `docs/L10N_001_LOCALIZATION.md`: the time helpers and the zone setting;
   - `docs/GATEWAY_RUNBOOK.md`: the headers rule, `/careers`, the names registry;
   - `docs/CI_AND_TESTING.md`: `InterfaceTextGuardTests`, and `Enforce` in the prod-like and E2E stacks;
   - `OPS_VPS_SINGLE_HOST.md`: what the demo users see, the closed postings with their undo, the backup location, the change table, the rollback note;
   - the «الحالة» lines of G-UX-01..10 in the study;
   - `docs/PROGRAM_COMPLETION_ROADMAP.md`: PLAN-67 as a side plan, and wave 2a numbered from PLAN-68 (§7, item 6);
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter (P7, if nothing it builds or tests changed since; otherwise again, with the VPN off);
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD (with `-SkipTestcontainers` when the VPN is on);
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

   All must be green. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan67-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations. No deviation defers part of the plan.
   4. **The CSP, before and after:** the cause P0 proved, and per key page the number of policies and the violations by kind, on the demo and on the local stack.
   5. **The interface, before and after:** per service and per kind (English labels, raw statuses, ids, times, JSON, inline code, empty tables), the inventory's counts, ending at zero except the named technical exceptions.
   6. **Names on the demo:** users the registry knows, and people still «غير معروف».
   7. **Readable rules:** rules rendered, and the parity results.
   8. **The portal and the demo data:** the links checked, and the postings closed, each with its undo.
   9. The guards and their own negative tests.
   10. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record, where the screenshots are.
   11. **The server:** before and after, changes and their undo, the backup location, the rollback note.
   12. Items that other plans own: POL-G, WF-H, D-67-6's reset.
   13. Defects for later plans.
   14. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 67`.
   15. **A readiness note:** for wave 2a, numbered from PLAN-68; and for POL-G, what the renderer gives the structured editor.
4. **Merge (P-19), by you, in the same turn as the pre-push check:**
   1. Merge the feature branch into a new `release/plan-67` from `main` (`--no-ff`).
   2. Write `reports/plan67-pr.md` (short).
   3. `git push -u origin release/plan-67`
   4. `gh pr create --base main --head release/plan-67 --title "PLAN-67: a readable Arabic interface, one content security policy per page, and a clean demo" --body-file reports/plan67-pr.md`
   5. Wait for the checks («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan67-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-67-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process (P-2).

## Definition of done

- **Every key page carries one CSP and raises no violation,** on the demo and on the prod-like stack, which now enforces it like Production.
- **No view carries an inline handler or a `style` attribute,** and no CSP was loosened.
- **Every confirmation shows** before its destructive action, and every reviewer's comment reaches its record.
- **`InterfaceTextGuardTests` is green with no rows,** only the technical exceptions named in its code.
- **Every time** shows in the configured zone, in one format.
- **Every policy rule reads as an Arabic sentence,** the simulation takes a form, and the JSON sits behind «عرض تقني» for D-67-4's roles.
- **The portal lives at `/careers` alone,** styled, with no test residue; `/ui/hr-core/` opens the dashboard.
- **Nothing in the plan was deferred to save time.**
- **The tests:** AC-UX-67a..f = Pass (the PR run's artifact is the evidence of record, with the screenshots), and the PLAN-36..52 suites are green.
- **The server** runs this code.
- **The documents** P10 lists are updated.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed, and what P0 found to be the cause of the unstyled portal;
- what the owner will see differently on the demo, and where the before and after screenshots are;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
