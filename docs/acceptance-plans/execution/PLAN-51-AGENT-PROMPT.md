# Task: PLAN-51 — An explicit policy for every action, and no wide paths or wide policies left (IAM-E)

The deliverables:
- **Self-service reaches only its own controllers.** Through `/ui/ess`, or any path with `/Ess` in it, a user with `hr-employee` reaches only the named self-service controllers, **even if he also holds an HR.Core read or approval role**.
- **No interactive user reaches a service endpoint or the staff directory:**
  - no `/api/internal/*` endpoint accepts an interactive user;
  - `hr-employee` and the exam marker cannot search the agency's employee directory.
- **The gateway's audit log** is for its owners only, and each agency sees its own rows.
- **Every write action in every service names its policy in the service itself,** so nothing depends on the gateway alone. Payroll waits for PLAN-53, which gives it its own sixteen policies.
- **One architecture test project covers all fifteen web assemblies,** with per-service ceiling lists that never grow. Every list ends empty, except what PLAN-53 and wave 2 own.
- **A service account with `hr-core.workflow`** starts a workflow, generates a code and queries a policy, and writes nothing in the employee records.
- **Agency onboarding is the platform administrator's alone,** and reads no file from disk over HTTP.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P9** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks.

**Complete execution** (owner, 2026-11-12): the owner wants this plan carried out in full, however long it takes.
- Time is never a reason to skip, defer or allowlist anything.
- Only what the plan itself assigns to another plan stays out: its «Out of scope», and the rows P-8 names.
- The instruction also goes beyond the plan in two places (P-6, P-8): every write-policy list ends empty except Payroll's, and the twelve entity bindings that PLAN-50 handed over are replaced by view models.

**Standing owner instruction on merges** (PLAN-38 to PLAN-50 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-15).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Check formatting on the files you changed (see «Lessons»).
4. Commit, using the phase's commit message.
5. Update **`reports/plan51-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan51-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan51-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep, GitHub), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in the Claude desktop app, at `xhigh`). PLAN-50 stopped once at the usage limit of the owner's plan. Economy is about tokens, never about doing less of the plan:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan51/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan51/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, `ci-local.ps1` and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn, as PLAN-49 and PLAN-50 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A background `gh run watch <id> --exit-status` is fine too, if this session's background commands wake it when they end.
- **Then act at once:**
  - every check green → merge (P-15);
  - a red check → read its log as soon as it fails, without waiting for the other checks; fix it on the release branch, push that branch only, and wait again the same way.
- **A run that never starts:** if the PR's run stays queued with no jobs for 15 minutes, look at GitHub's status page. During an Actions incident such a run can be neither cancelled nor rerun. Close and reopen the PR to start a new run on the same HEAD, as PLAN-50 did, and record it.
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

You are a senior .NET engineer for authorization on multi-tenant platforms:
- ASP.NET Core authorization: policies, requirements and handlers, MVC filters, a gateway that authorizes by path;
- architecture tests over many assemblies, by reflection, with ceiling lists that only shrink;
- service-to-service trust: client credentials, scopes, service-only routes;
- the multi-tenant audit trail of a reverse proxy, on SQLite and PostgreSQL;
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..50 did.

**Where things stand** when this prompt was written (2026-11-12):
- **`main`** = `0db7292e`. It holds PLAN-50 (PR 48).
- **PLAN-50's merge note** is a single commit, `8ffb2471`, on the local branch `docs/plan-50-merge-note`, not yet on `main`. The main folder is checked out on that branch.
- **The demo server** runs `a504f6e54921`, which is the product code of `main`: only tests, documents and the gitleaks allowlist changed after it.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 15 checks and took 66 minutes in PLAN-50 (CI itself 47 minutes, the longest); one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the stage jobs are `lab-main`, `lab-rest`, `prodlike`, `prodlike-policy`, `prodlike-final`, `leastpriv` and `meta`;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new acceptance class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs;
  - **the build and test steps live in `scripts/ci/steps.json`,** which CI and `ci-local.ps1` both read; `CiStepsParityTests` keeps `ci.yml` in line with it.
- **From PLAN-50** (its final report, §3, §6, §8–§12):
  - **The attribute:** `[ModulePolicyOverride(policy)]` replaces the module policy for one action or controller, and `ModulePolicyOverrideValidator` stops the boot on a policy that is not registered. The module filter passes the `HttpContext` as the resource of its policy check.
  - **The gateway table:** `GatewayHrCoreActionPolicies` holds HR.Core's gateway exceptions for approval actions, among them Onboarding's two actions (`apply`, `apply-file`, under `HrCore.Approve`).
  - **"Interactive staff":** `Gateway.ApprovalReach` and `GatewayApprovalReachAuthorization` define it. A user is staff when he holds a platform role that is not in `Authorization:NonStaffRoles` (`hr-employee`, `hr-exam-marker`, `applicant`). Keycloak's own roles (`offline_access`, `uma_authorization`, `default-roles-*`) never count.
  - **Its guards:** `GatewayApproverReachTests` and `Iam050MovementApprovalPolicyTests` (41 movement actions, six roles).
  - **`MassAssignmentArchitectureTests`** now scans HR.Movements.Web and pins twelve older entity bindings. PLAN-50 sent them to this plan, and the owner wants them done here, although the plan puts view models in wave 2 (P-8).
  - **`HrCore.BranchOperation`** has no user left: PLAN-50 moved the workflow actions to `Workflow.StepAction`.
  - **Its low defects** (report §10) are not this plan's.
- **The code today** (read on `0db7292e`). The plan's line numbers date from 2026-10-30 and have moved since PLAN-42..50: find the code by its content.
  - **The module filter's name list** (`HrModuleAuthorizationFilter`) lets these controllers skip the module policy in every service:
    - any name that starts with `Ess`, and `EssPortal`;
    - `HireWizard`, `LookupApi`, `CodeGeneration`, `Policies`, `PolicyTemplates`, `Onboarding`, `TenantQuarantine`;
    - `EmployeeLifecycleInternal`, `DirectoryInternalSync`;
    - `RecruitmentAppeals`, `WorkInbox`, `WorkflowApprovals`, `ExamMarking`, `WorkflowDefinitions`.
  - **Self-service controllers:**
    - HR.Core `EssPortal` and `EssTalent`, both under `[Authorize(Module.Ess)]` with POST actions;
    - Payroll `Ess`;
    - HR.Training `EssLearning`. Only the name list lets it skip its module today, and the gateway gives `/ui/hr-training/...` the training module's policy.
  - **The gateway's self-service rule** (`TryGetEssPolicy`) matches any path that **contains** `/Ess` under `payroll`, `hr-core` and `ess`. It runs before the fine-grained rules and before PLAN-50's table. So every `/ui/ess/...` path gets `Module.Ess`, and the route `ui-ess` sends `/ui/ess/{**catch-all}` to HR.Core.
  - **The lookup:**
    - `/api/lookup` (matched with `Contains`) and HR.Core's static files get `HrCore.Lookup`, which any interactive user or a service with `hr-core.lookup` passes;
    - the shared `display-names.js` and `entity-pickers.js` call the lookup API (the first candidates for inventory 0.6).
  - **Internal routes:**
    - `Service.HrCoreLookup` no longer exists: PLAN-42 replaced it with `Service.HrCoreInternal` and `Service.HrCoreLifecycle`, both service-only;
    - all ten controllers under `api/internal` carry a `ServicePolicies.*` policy at class level.
  - **The gateway's audit:**
    - `HomeController.Operations`, `Audit` and `AuditSearch` (`/ops/audit-search`) sit under the gateway's global `Gateway.Dashboard`, which any signed-in user passes;
    - `GatewayAuditQueryService` filters no tenant, and `GatewayAuditEntry.TenantId` is nullable.
  - **The policies:**
    - `HrCore.Write`: Admin, `hr-core`, or a service with `hr-core.workflow`;
    - `Module.HrCore` and `HrCore.ReadOnly` admit services with `hr-core.lookup` or `hr-core.workflow`;
    - `WorkflowExternalStart`: a service with `hr-core.workflow`, on `POST api/workflow-instances`;
    - `HrContracts.Approve`: Admin, `hr-contracts`;
    - `Payroll.Post`: Admin, `payroll-poster`. `Payroll.Reverse`: Admin, `payroll-poster`, `payroll-reverser`. `HrHealth.Clinical`: Admin, `hr-health-clinical`;
    - `AddHrModuleAuthorizeFilter` takes one policy, and there is no `AddModulePolicies`.
  - **Tests:**
    - `tests/architecture/hrcore-post-policy-allowlist.txt` still holds 42 actions;
    - `HrCorePostActionsHaveExplicitPolicyTests` and `S2s042InternalRoutesAreServiceOnlyTests` live in `HRManagement.UnitTests/Architecture`;
    - the unit test project references four web assemblies (HR.Core, HR.Contracts, HR.Movements, Admin.Logistics), and the integration project seven and the gateway.
  - **Onboarding:**
    - `api/onboarding/agencies/apply` and `apply-file`, both under `HrCore.Approve` in the service and in PLAN-50's table;
    - AC-07 (lab, not required) onboards as `platform-admin`.
  - **`Service.EvidenceIngest`** checks the service and its scope, with no client list. The gateway's `EvidenceArtifactsController` uses it.
  - **Personas:**
    - there are 65. `b-auditor`, `a-exam-marker`, `a-payroll-poster`, `platform-admin`, `central-a`, `employee-a`, `a-supervisory-reader` and `a-management-approver` exist;
    - the plan's two new personas bring 67;
    - only `a-directory-checker` holds `hr-admin`.
  - **`AcceptanceHarness.CreateServiceClientAsync(clientId, scope, recorder, tenant, actor?)`** exists (PLAN-42).
  - **The demo server:**
    - "demo mode" is `Deployment:Mode=demo`, set by `docker-compose.vps-demo.yml`; the environment stays Production;
    - `hradmin` and `e2e-hr-admin` hold `hr-admin`; no demo user holds `hr-employee`, `platform-admin` or `auditor`; `e2e-compliance` holds `compliance`;
    - `AcOps36VpsBootTests` and `AcWf43VpsChecks` sign in as `hradmin`.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). `WF044_WorkflowInstanceIntegrity` shows the way: lift `FORCE` on the tables it reads inside the migration's own transaction, then restore it.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-43, PLAN-44, PLAN-47..50).
  - **A new PostgreSQL test uses the connection strings of `PostgreSqlFixture`,** whose idle connections close after seconds. A class that passes alone but fails in the full Testcontainers step is a real failure (PLAN-49, `docs/CI_AND_TESTING.md`).
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in `appsettings.json` loads everywhere; a value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
  - **Personas and users:**
    - a persona change needs `scripts/acceptance-reset.ps1` before the first start (PLAN-43);
    - a new persona also changes `DATA-AUTH` in `scripts/acceptance-verify.ps1` (PLAN-48);
    - a user added to the manifest reaches a live realm only through `scripts/vps/add-demo-user.sh` (PLAN-45).
  - An EF migration file must not carry a byte order mark: the format gate refuses it (PLAN-45).
  - **Formatting:** before every commit, run `dotnet format whitespace HRManagement.slnx --verify-no-changes --no-restore --include <the files you changed>`. CI runs the full check (`dotnet format HRManagement.slnx --verify-no-changes --no-restore`), and in PLAN-50 it failed ci-local twice on whitespace, about 17 minutes each time.
  - **Acceptance tests:**
    - a test posts the URL and the token the page renders (PLAN-44);
    - policy API writes need the antiforgery token (`AcceptanceHarness.CreatePolicyLifecycleClientAsync`, PLAN-47).
  - **The machine:**
    - after the host sleeps, the Docker VM's clock lags, and time-based tests fail until it catches up (PLAN-50);
    - when the owner's VPN is on, ci-local's Testcontainers fail with "Failed to connect to 192.168.65.254". Run those classes on the host, then `ci-local.ps1 -SkipTestcontainers`. That is allowed, because Testcontainers run on ubuntu-latest in CI (PLAN-48).
  - **The server:**
    - run the server build detached (PLAN-44), and the server suites with nothing heavy beside them (PLAN-45);
    - if the server build fails because the provider's resolvers answer SERVFAIL, wait and retry; never change the server's resolver configuration (PLAN-50);
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan51/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49).
  - **Before the push:**
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql (PLAN-48..50);
    - run the full Testcontainers step on the host with CI's filter, when `ci-local.ps1` runs with `-SkipTestcontainers` (PLAN-49);
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging` (PLAN-48);
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it, and never quote a flagged value in a report (PLAN-48, PLAN-50).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-51-iam-explicit-policy-every-action.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. These reports:
   - `reports/plan50-final-report.md`, in full;
   - the authorization and service-trust parts of `reports/plan42-final-report.md` (service-only routes, scopes, the tenant of a service call) and `reports/plan37-final-report.md` (the wiring matrix);
   - the CI parts of `reports/plan49-final-report.md`.
3. These documents:
   - `docs/IAM_001_SCOPE_GAP_STUDY.md` (G-IAM-05, G-IAM-06, G-IAM-08, G-IAM-09, §2.2, and the independent verification's new finding 1);
   - `docs/GATEWAY_RUNBOOK.md`, `docs/SERVICE_TRUST_INVENTORY.md`, `docs/ESS_001_SERVICE_CENTER.md`, `docs/HIRE_RBAC_MATRIX.md`, `docs/SEED_001_MASTER_DATA.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
6. `docs/OWNER_DECISIONS_REGISTER.md`: D-51-1 to D-51-5, D-IAM-4, D-IAM-7.
7. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`,** on `main` or on `docs/plan-50-merge-note` (where PLAN-50 left the folder), and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-50 (`0db7292e` or later).
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

- **D-51-1, `/ui/ess`:** it stays, because the user guide names it, but it passes only the named self-service controllers and the static files (GET). Any other path under it gets a 404 from the gateway.
  - `Gateway:Ess:AllowedControllers`, per segment: `ess` and `hr-core` = `EssPortal`, `EssTalent`; `payroll` = `Ess`.
  - Adding `hr-training`/`EssLearning` (G-IAM-15) is one configuration line, in wave 2.
- **D-IAM-7, who reads the gateway's audit:** `platform-admin` for every agency; `auditor` and `compliance` for their own agency only (`Gateway:Audit:Roles`). `hr-admin` no longer sees the log.
- **D-IAM-4, `hr-admin`:**
  - `hr-admin` is excluded from `HrHealth.Clinical`, `Payroll.Post` and `Payroll.Reverse` through `Authorization:AdminExcludedPolicies`, in Production, Staging and the prod-like file;
  - Development and the demo have no exclusion;
  - splitting the role and a «break glass» with MFA come in wave 2 (IAM-G).
- **D-51-2, write and approval roles per module:**
  - two policies per module: `{Module}.Write` and `{Module}.Approve`, with roles from `Authorization:Policies:{name}:Roles`;
  - the default is today's behaviour (the module's role), except HR.Core (`HrCore.Write` = Admin, `hr-core`) and HR.Contracts (`HrContracts.Write` = Admin, `hr-contracts`).
- **D-51-3, `HrCore.Lookup`:** every interactive user with a staff role (PLAN-50's `Authorization:NonStaffRoles` leaves out `hr-employee`, `hr-exam-marker` and `applicant`), and services with `hr-core.lookup`. Neither the employee nor the exam marker searches the directory; the other modules' pickers keep working for their staff.
- **D-51-4, narrow S2S scopes:** no new scopes in this plan.
  - `hr-core.workflow` is accepted on three endpoints only: starting a workflow, generating a code, and querying a policy;
  - service accounts leave `HrCore.Write` (and `HrCore.BranchOperation`, which PLAN-43 already closed to them);
  - no change in the realm or the compose files.
- **D-51-5, evidence ingest:** a client list from `Evidence:IngestClients`, empty by default (nobody). There is no caller in the repository today.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-51-iam-explicit-policy-every-action` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-50's merge note from `docs/plan-50-merge-note` (`8ffb2471`);<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine: awake, Docker, the disk, the ports.**<br>• **Keep the machine awake for the session, changing no setting.** At P0, start a hidden background PowerShell process that holds `SetThreadExecutionState(ES_CONTINUOUS \| ES_SYSTEM_REQUIRED)` and sleeps in a loop. Record its PID, and stop it at the end of the session. It does not stop a Windows Update restart; record one if it happens.<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run, check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*`, Testcontainers, and untagged images of this repository's own builds. Never prune volumes or images of other projects.<br>• **The disk:** before every image build and every stack start, read the free space of the drive that holds Docker's data disk.<br>&nbsp;&nbsp;– Below 15 GB, remove this repository's leftovers first.<br>&nbsp;&nbsp;– If it is still below 15 GB, moving Docker's disk is a setting of the owner: `BLOCKED:`.<br>• **One heavy job at a time:** never an image build beside a host test run.<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** compare the stack's host ports with `netsh interface ipv4 show excludedportrange protocol=tcp`. If a port the tests read is reserved, freeing it is a system change for the owner: `BLOCKED:` with the range. |
| P-3 | **Self-service through the gateway (plan §1).**<br>• **`GatewayEssOptions` (`Gateway:Ess`):** the allowed controllers per segment (D-51-1), and the static prefixes the plan lists.<br>• **`TryGetEssPolicy`** matches the **first segment** after `/ui/{segment}/` or `/api/{segment}/` exactly, never with `Contains`.<br>&nbsp;&nbsp;– An allowed controller gets `Module.Ess`.<br>&nbsp;&nbsp;– On the `ess` segment, anything that is not an allowed controller or a static file (GET) gets a 404 before forwarding. The gateway's audit records it as `Denied:EssNotExposed`.<br>&nbsp;&nbsp;– On the other segments, no match means the usual rules apply, PLAN-50's table first.<br>• **The order:** the self-service decision on the `ess` segment is final. Nothing under `/ui/ess` reaches PLAN-50's table or `Gateway.ApprovalReach`.<br>• **`GatewayEssRoutingTests`:** the plan's table of cases, plus look-alike paths (`/ui/hr-core/Essentials/…`, `/ui/ess/EssPortalX/…`, `/ui/ess/essportal/…` with odd casing), and the approval paths of PLAN-50 under `/ui/ess` (404).<br>• **HR.Training's `EssLearning`** stays outside the gateway's list (wave 2), and keeps its explicit self-service policy in the service (P-6). |
| P-4 | **Service endpoints, the directory, the static files (plan §2).**<br>• **`Service.HrCoreLookup` is already gone** (PLAN-42). Record that §2's first row needs no change, and keep the rule it served: no `api/internal` route accepts an interactive user. `InternalRoutesAreServiceOnlyTests` (P-8) proves it over all fifteen assemblies.<br>• **`HrCore.Lookup`:** interactive staff, or a service with `hr-core.lookup` (D-51-3).<br>&nbsp;&nbsp;– Use PLAN-50's staff definition (`GatewayApprovalReachAuthorization`, `Authorization:NonStaffRoles`), never a second one.<br>&nbsp;&nbsp;– Keycloak's own roles never make a user staff.<br>• **`Gateway.Authenticated`** for HR.Core's static files, instead of `HrCore.Lookup`, so the self-service and marking pages keep their styles.<br>• **Inventory 0.6 decides the rest.** Start with `display-names.js` and `entity-pickers.js`, on every page that `hr-employee` or `hr-exam-marker` reaches.<br>&nbsp;&nbsp;– A lookup call such a page needs is replaced by an ownership endpoint in the self-service controller, never by widening the policy.<br>&nbsp;&nbsp;– A call it does not need is not made on that page. |
| P-5 | **The gateway's audit (plan §3).**<br>• **`Gateway.Audit`:** the roles from `Gateway:Audit:Roles` (D-IAM-7), on `Operations`, `Audit` and `AuditSearch`.<br>• **The tenant filter is in `GatewayAuditQueryService`, not in the view:**<br>&nbsp;&nbsp;– the caller's `tenant_id` filters the rows, except for `platform-admin`;<br>&nbsp;&nbsp;– without a valid `tenant_id`, no rows;<br>&nbsp;&nbsp;– rows with no `TenantId` are for `platform-admin` only.<br>• **The menu link** «التدقيق» shows only to a user who has the policy.<br>• **Tests:** the filter on SQLite and on PostgreSQL (`Iam051GatewayAuditPostgreSqlTests`, three agencies and rows with no agency, with `PostgreSqlFixture`'s connection strings). |
| P-6 | **The module filter, the write policies, the explicit policies (plan §4).**<br>• **The name list goes.** Each controller it served (inventory 0.7) gets one of two things:<br>&nbsp;&nbsp;– a `Service.*` policy;<br>&nbsp;&nbsp;– or `[ModulePolicyOverride(policy)]`, naming the policy every action of that controller already requires: its class-level `[Authorize]`, or a policy built from the roles its actions share. Never a policy wider than what the controller admits today.<br>• **The override never authorizes a write by itself.** A write action under an override, on the action or on its class, also carries its own explicit policy, on the action or on the class. An override placed on a write action itself is allowed only with an approval policy (PLAN-50's approvals). `ModuleOverridesAreDeclaredTests` checks exactly this. PLAN-50's movement test stays as it is.<br>• **The default write policy:** a POST, PUT, PATCH or DELETE on an action with no explicit policy on the action or its class also meets the module's write policy, after whichever policy the filter applied, the override included. `AddHrModuleAuthorizeFilter(modulePolicy, writePolicy)` takes it, and the fourteen `Program.cs` pass `{Module}.Write`.<br>• **`{Module}.Write` and `{Module}.Approve` come from configuration:**<br>&nbsp;&nbsp;– one helper, `RolesPolicy(name, defaults)`, also applies `Authorization:AdminExcludedPolicies`;<br>&nbsp;&nbsp;– the defaults are today's roles, so nothing changes in a single-role module (D-51-2).<br>• **D-IAM-4 on the demo:** the exclusion lives in the Production and Staging files of the services concerned, and in the prod-like mirror. It is off on a host with `Deployment:Mode=demo`. Put that rule in code, beside the configuration guard's demo check; an empty array cannot be set reliably through environment variables. Prove both: the prod-like stack excludes, demo mode does not.<br>• **HR.Core:**<br>&nbsp;&nbsp;– each of the 42 actions in the list gets `HrCore.Write` explicitly;<br>&nbsp;&nbsp;– `Signatures.Verify` gets `HrCore.ReadOnly`, and `ExportJobs.Cancel` gets `HrCore.ReadOnly` with today's owner check;<br>&nbsp;&nbsp;– the list is emptied, then deleted.<br>• **HR.Contracts,** every controller except `HireWizard`: `HrContracts.Write` on the classes for writes, the existing `HrContracts.Approve` on approvals, and `Module.HrContracts` explicitly for reads.<br>• **The sensitive actions in the other services:** the plan's list, from inventory 0.4.<br>• **`HrCore.BranchOperation`:** delete it if nothing uses it, tests included, and record it.<br>• **No fallback for size** (owner, 2026-11-12): the plan's Risks, last row, is not used. Every sensitive action in the plan's list gets its approval policy, whatever the time.<br>• **Beyond the plan (owner, 2026-11-12): every write action in every service names its policy,** not only in HR.Core and HR.Contracts. The filter's default write policy becomes a safety net, never the only guard.<br>&nbsp;&nbsp;– In a single-role module, the explicit policy is `{Module}.Write`, or `{Module}.Approve` for a sensitive action. Those are the same roles the default applies, so nobody's access changes.<br>&nbsp;&nbsp;– Payroll alone stays as the plan says: PLAN-53 gives it its sixteen policies. |
| P-7 | **Service accounts, onboarding, evidence (plan §5).**<br>• **`HrCore.Write` loses its service branch.**<br>&nbsp;&nbsp;– `hr-core.workflow` then reaches only `WorkflowExternalStart`, `ServicePolicies.CodeGenerate` and `ServicePolicies.PolicyEnforceS2S`, besides the reads that §5's rule 1 keeps.<br>&nbsp;&nbsp;– Inventory 0.5 comes from the code **and** from `deploy/wiring/service-wiring.json`.<br>&nbsp;&nbsp;– Each client in it gets an integration test.<br>&nbsp;&nbsp;– Update `serverPolicy` for every `calls[]` entry that went through `HrCore.Write`; `ProductionWiringMatrixTests` stays green.<br>• **A service that still calls `api/codes/generate`** (`CodeGenerationController`, `HrCore.Write`) moves to the internal endpoint of PLAN-42.<br>• **Onboarding:**<br>&nbsp;&nbsp;– `Platform.AgencyOnboarding` (`platform-admin` only), in the service and in PLAN-50's gateway table;<br>&nbsp;&nbsp;– `apply-file` is deleted, from the controller and from the table;<br>&nbsp;&nbsp;– `AgencyManifestLoader` stays for the scripts and the seeds.<br>• **`Service.EvidenceIngest`:** `IsAllowedClient` from `Evidence:IngestClients` (D-51-5). If inventory 0.5 finds a caller after all, list it in the configuration and record it. |
| P-8 | **The architecture test project (plan §6).**<br>• **The project:** `src/Tests/HRManagement.ArchitectureTests`, in `HRManagement.slnx` and in `scripts/ci/steps.json` beside the unit step, so that CI and `ci-local.ps1` both run it (`CiStepsParityTests` stays green).<br>&nbsp;&nbsp;– It collects no coverage unless the coverage gate needs it; the gate's numbers must not move because of it.<br>&nbsp;&nbsp;– Record what it adds to the build time.<br>• **`WebAssemblyCatalog`** lists the fifteen assemblies (fourteen services and the gateway). `AllWebAssembliesLoadTests` fails if one is missing; an assembly that does not load fails a test, it is never skipped.<br>• **The tests:**<br>&nbsp;&nbsp;– `WriteActionsHaveExplicitPolicyTests`: per-service ceiling lists that never grow. Seeded in P1, they **end empty in every service except Payroll**, which stays frozen for PLAN-53 (P-6);<br>&nbsp;&nbsp;– `SensitiveActionsHaveApprovalPolicyTests`: it matches whole action names, or the plan's prefixes, never substrings. Its list ends empty, except Payroll rows that PLAN-53 owns;<br>&nbsp;&nbsp;– `InternalRoutesAreServiceOnlyTests`: moved from `S2s042…`, with one rule and no second test;<br>&nbsp;&nbsp;– `ModuleOverridesAreDeclaredTests` (P-6).<br>• **`HrCorePostActionsHaveExplicitPolicyTests`** and its list are deleted. PLAN-25's rule («no recruitment controller in the list») lives on in the general test.<br>• **`MassAssignmentArchitectureTests`** moves too, so that it covers all fifteen assemblies.<br>&nbsp;&nbsp;– **PLAN-50's twelve bindings in HR.Movements.Web are replaced by view models** (owner, 2026-11-12), so HR.Movements leaves the list. The forms keep working; `AcInt37`, `AcMsg40` and `AcIam50` prove it.<br>&nbsp;&nbsp;– A binding the scan newly finds in another assembly is pinned in a list that never grows, with its reason, and the report gives the count per service. View models there are wave 2 (IAM-F, G-IAM-10), as the plan's scope says.<br>• **A row in any list is allowed only for what another plan owns** (Payroll for PLAN-53; entity bindings outside HR.Movements for IAM-F), and carries that reason. Never a row to save time. PLAN-65 reviews them. |
| P-9 | **Existing tests that the new rules change.**<br>• A test changes to the new rule if it relied on:<br>&nbsp;&nbsp;– the broad `/Ess` match;<br>&nbsp;&nbsp;– `HrCore.Lookup` for any user;<br>&nbsp;&nbsp;– `HrCore.Write` for a service account;<br>&nbsp;&nbsp;– `apply-file`, or onboarding by `hr-core`;<br>&nbsp;&nbsp;– `hr-admin` reading the gateway's audit;<br>&nbsp;&nbsp;– the name list.<br>&nbsp;&nbsp;Never weaken an assertion.<br>• **`GatewayPathAuthorizationMiddlewareTests`** is updated for the moved rules; every refusal it proved before, it still proves.<br>• **Stay green unchanged:** AC-07 (it onboards as `platform-admin`), AC-HCM-25, AC-HCM-28, AC-HCM-31, AC-IAM-50, `GatewayApproverReachTests` and `Iam050MovementApprovalPolicyTests`. |
| P-10 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• **`AcIam51ExplicitPolicyTests`** (AC-IAM-51a..f): `prodLike[]`, `required: true`, group `main`. It must not stop or recreate containers.<br>• **AC-IAM-51c makes its own audit rows** for agencies A and B (one request as a persona of each) before it reads them. It does not rely on what other classes left.<br>• **The two new personas** (`a-ess-supervisory`, `a-ess-approver`), each with an employee in agency A:<br>&nbsp;&nbsp;– `personas.json` and the persona catalog;<br>&nbsp;&nbsp;– the realm generator, `AcceptanceV2CatalogTests` and `AcceptanceIdentityPopulationTests`;<br>&nbsp;&nbsp;– `DATA-AUTH`, which becomes 67;<br>&nbsp;&nbsp;– `scripts/acceptance-reset.ps1` before the first local start.<br>• **Service-account scenarios** use `AcceptanceHarness.CreateServiceClientAsync`. Secrets come from `ACCEPTANCE_SVC_<CLIENT>_SECRET`, and never reach the evidence or the repository.<br>• `ac-ownership.json`: `AC-IAM-51` (owner PLAN-51, `provesClaim: true`).<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-IAM-51/` as the plan says. Copy what the report needs into `artifacts/plan51/`, then restore the committed evidence. |
| P-11 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`, and the overlay groups with `-Stage prodLike:<group>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcIam51ExplicitPolicyTests`, `AcIam50ApproverReachDelegationTests`, `AcHcm25ProductionApprovalTests`, `AcWf43DefinitionGovernanceTests`, `AcTen48NationalScopeTests`, `AcPol49EngineScopeTests`, `AcInt37ProductionWiringTests`, `AcMsg40AtomicPublishingTests`, `AcWf45AssetApprovalLoopTests`, `AcWf46CrossServiceApprovalTests`, `AcPol47RuleLifecycleTests`, and the prod-like journey;<br>• **the `s2s42` group** (`AcS2s42TrustBoundaryTests`, `AcWf46PolicyOrphanTests`), because service accounts change;<br>• **lab:** `Ac05PlatformEnterTenantTests`, `Ac07AgencyOnboardingTests`, `AcHcm21RequisitionApprovalUiTests`, `AcHcm22RecruitmentWorkflowsUiTests`, `AcHcm28CommitteeAppealTests`, `AcHcm31CompetitionTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcWf23WorkInboxUiTests`, the lab `isolated` group (`AcHcm26`, `AcHcm27`), and the lab journey;<br>• **leastpriv:** `AcOps38FirstBootTests`, `AcTen48LeastPrivilegeTests`;<br>• and any class that inventory 0.4 finds posting, as its persona, to an action whose policy changes.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-12 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• **`scripts/iam051-validate.ps1`** holds no Arabic literal and has a `-SelfTest` with planted defects, as `iam050-validate.ps1` does. It checks, at least:<br>&nbsp;&nbsp;– no `Contains("/Ess")` in the gateway;<br>&nbsp;&nbsp;– no name list in the filter;<br>&nbsp;&nbsp;– the ceiling lists and their reasons;<br>&nbsp;&nbsp;– the three endpoints of `hr-core.workflow`;<br>&nbsp;&nbsp;– no `apply-file`.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`, not with the plan's raw `docker compose` lines. |
| P-13 | **The demo server facts this plan must respect.**<br>• It runs the Production environment in demo mode (`Deployment:Mode=demo`): D-IAM-4's exclusion is off there (P-6), and `hradmin` keeps what it has.<br>• **What the owner will see change:**<br>&nbsp;&nbsp;– `hradmin` and `e2e-hr-admin` lose the gateway's audit pages (D-IAM-7), and `e2e-compliance` gains them, with its own agency's rows;<br>&nbsp;&nbsp;– no demo user holds `platform-admin`, so no demo user can call the onboarding API, and the scripts and seeds keep `AgencyManifestLoader`;<br>&nbsp;&nbsp;– no demo user holds `hr-employee`, but the gateway's 404 on `/ui/ess` holds for any user, so a server check can prove it.<br>• **P0 lists, read-only:**<br>&nbsp;&nbsp;– the gateway's audit rows per agency, and the rows with no agency;<br>&nbsp;&nbsp;– the services' `Authorization` and `Gateway` configuration as the running containers see it. |
| P-14 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan51-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-13's picture.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`.<br>• **Rollback safety:** find out what PLAN-50's tag (`a504f6e54921`) does with this plan's data and configuration. Record it in `OPS_VPS_SINGLE_HOST.md`.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-50's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-15 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P9, after local CI and the pre-push check are green.<br>2. `gh pr create`, then wait in the session («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-16 | **No regression of PLAN-25, PLAN-36..50 and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests`;<br>• `AcIam50`, `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• the `Iam050*`, `Wf043*`, `Wf044*`, `S2s042*`, `Pol047*`, `Ten048*` and `Pol049*` tests, `GatewayApproverReachTests`, `GatewayPathAuthorizationMiddlewareTests`, `ProductionWiringMatrixTests`, `CiStepsParityTests`, `WorkflowDefinitionsHaveHandlersTests`, `Qa066*`;<br>• `scripts/iam050-validate.ps1`, `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/pol049-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-50, done:** the approver's reach through the gateway, the `HrCore.Approve` exceptions, delegation. This plan uses them.
- **PLAN-53:** the sixteen payroll policies, and emptying the Payroll list.
- **PLAN-37 and PLAN-42, done:** service addresses, scopes and secrets; the trust boundary between services.
- **Wave 2, IAM-G:**
  - a token audience per module;
  - splitting `hr-admin`, and «break glass» with MFA;
  - new roles that separate the reader from the writer in a module.
- **PLAN-52:** the branch scope, and object-level access (BOLA).
- **Wave 2, IAM-F:**
  - creator ≠ approver in training, distribution and discipline;
  - a second approver for granting sensitive roles;
  - the national number, and view models outside HR.Movements (G-IAM-07, G-IAM-10). PLAN-50's twelve bindings in HR.Movements are done here (P-8).
- **Wave 2, IAM-H:** learning self-service (`/ui/hr-training/EssLearning`) and the payslip. Only its configuration line is prepared here.

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
- add a scope or a client to the realm, or change a scope in a compose file (D-51-4);
- widen a policy to make a page or a test work;
- give a user a Keycloak role to make a scenario pass;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **A legitimate write of today cannot keep working** without giving a role more than it has today.
- **A service-to-service call cannot keep working** once `hr-core.workflow` leaves `HrCore.Write`, without a new scope (D-51-4).
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning.
- **The space for Docker's disk stays below 15 GB, or a host port the tests read is reserved** (P-2).
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, keep-awake, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Start the keep-awake process (P-2). Check the Preconditions.
2. `git switch -c feature/plan-51-iam-explicit-policy-every-action origin/main`. Bring in PLAN-50's merge note and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan51-baseline.md`:
   - **0.2 and 0.3:** the build, and the listed filter;
   - **0.4, the writes:** every POST, PUT, PATCH and DELETE in the fifteen assemblies without an explicit policy or `[AllowAnonymous]`, per service, against the study's §2.2. Also list which roles reach each one today, through the module policy and the gateway. This table seeds the ceiling lists;
   - **0.5, service calls:** every HTTP client between services, from the code and from the wiring matrix — the endpoint in HR.Core, its policy, and the scope it needs;
   - **0.6, the pages of employees and markers:** every call to `/api/lookup`, `/api/internal` or HR.Core's static files from a page that `hr-employee` or `hr-exam-marker` reaches;
   - **0.7, the name list:** every controller in every service that the list matches, with the policy it will carry.
4. **The server, read-only:** the running tag, and P-13's picture.

Commit: `chore(iam051): baseline, the writes without a policy per service, service calls, the pages of employees and markers, the name list, the server's audit rows and configuration`.

## P1 — The architecture test project (plan §6, P-8)

- **The project, the catalog of fifteen assemblies, and the four tests.**
- **The moves:** `S2s042InternalRoutesAreServiceOnlyTests` and `MassAssignmentArchitectureTests` move in; `HrCorePostActionsHaveExplicitPolicyTests` waits for P4, which empties its list.
- **The ceiling lists, seeded from inventory 0.4,** each row with its reason.
- **The step in `scripts/ci/steps.json`.**
- **Tests:** the project green on today's code with today's lists; each test fails on a planted defect.

Commit: `test(iam051): one architecture project over the fifteen web assemblies; writes, sensitive actions, internal routes, overrides and entity bindings checked against ceiling lists that only shrink`.

## P2 — Self-service, the directory, the static files (plan §1, §2, P-3, P-4)

- **The gateway:** `GatewayEssOptions`, the exact match, the 404 and its audit decision.
- **The policies:** `HrCore.Lookup` for staff and services, and `Gateway.Authenticated` for HR.Core's static files.
- **The pages:** the fixes that inventory 0.6 calls for.
- **Tests:** `GatewayEssRoutingTests`, the lookup policy with Keycloak's own roles, a service account and each non-staff role, and `GatewayPathAuthorizationMiddlewareTests`, updated.

Commit: `feat(iam051): self-service reaches its own controllers only; the employee directory is for staff and services; internal routes stay service-only`.

## P3 — The gateway's audit (plan §3, P-5)

- **The audit:** `Gateway.Audit`, the three pages, the tenant filter, the menu link.
- **Tests:** the filter on SQLite, and `Iam051GatewayAuditPostgreSqlTests`.

Commit: `feat(iam051): the gateway's audit log is for its owners, and each agency sees its own rows`.

## P4 — The filter, the write policies, the explicit policies (plan §4, P-6)

- **The filter:** no name list, the overrides, and the default write policy.
- **The policies:** `AddHrModuleAuthorizeFilter(module, write)` in the fourteen `Program.cs`, and the policies from configuration with D-IAM-4.
- **The services:** HR.Core's 42 actions, HR.Contracts, the sensitive actions, every other write in every service except Payroll (P-6), and `HrCore.BranchOperation`.
- **HR.Movements:** the twelve entity bindings replaced by view models (P-8).
- **The lists:** shrunk as each action gets its policy, down to Payroll's rows; HR.Core's old list and its test deleted.
- **Tests:**
  - `Iam051DirectServiceWriteTests`: directly to the service, as in `Hcm025ScopeAndPolicyTestHost`. `hr-employee,hr-core-supervisory` gets 403 on each of the 42 former actions (generated from the old list), and an interactive user gets 403 on `api/internal/*`;
  - D-IAM-4: excluded in the prod-like configuration, not in demo mode.

Commit: `feat(iam051): no module name list; every write in every service names its policy (Payroll waits for PLAN-53), sensitive actions their approval policy; HR.Movements binds view models, not entities`.

## P5 — Service accounts, onboarding, evidence (plan §5, P-7)

- **Service accounts:** `HrCore.Write` without its service branch, the three endpoints of `hr-core.workflow`, and the wiring matrix.
- **Onboarding:** `Platform.AgencyOnboarding`, and `apply-file` deleted.
- **Evidence:** `Evidence:IngestClients`.
- **Tests:**
  - an integration test per client from inventory 0.5;
  - the onboarding policy in the service and at the gateway;
  - the evidence client list;
  - `ProductionWiringMatrixTests`.

Commit: `feat(iam051): hr-core.workflow reaches three endpoints only; agency onboarding is the platform administrator's and reads no file; evidence ingest takes a client list`.

## P6 — Acceptance, then the independent review (plan Acceptance, P-9, P-10, P-11)

1. **The pieces of the acceptance run** (P-10): the class, the two personas and their counts, `ci-scope.json`, `ac-ownership.json`, `scripts/iam051-validate.ps1`.
2. **Acceptance.**
   1. Check the disk (P-2). Run `scripts/acceptance-reset.ps1` (new personas), start the stack (`acceptance-up.ps1 -Profile ProdLike`), and record the time to ready.
   2. Run `AcIam51ExplicitPolicyTests` (AC-IAM-51a..f), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then run P-11's list, `scripts/iam051-validate.ps1` (and `-SelfTest`), and the seeded counts by psql. All required classes must Pass.
   4. `down -v`. Restore the committed evidence.
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a path under `/ui/ess` that still reaches a controller outside the list, or an `/Ess` match left anywhere in the gateway;
   - a write action anywhere that a read role or a non-staff role still reaches in the service;
   - an override that authorizes a write by itself, or a policy wider than the controller admitted before;
   - a service account that still writes in HR.Core outside the three endpoints;
   - an interactive user who reaches an `api/internal` route or the directory without a staff role;
   - an audit row of one agency visible to another, or a row with no agency visible to anyone but `platform-admin`;
   - a ceiling list that grew, a row without a reason, or a row that no other plan owns;
   - a part of the plan that was deferred, skipped or allowlisted to save time;
   - something PLAN-50's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(iam051): AC-IAM-51a..f pass on the prod-like stack; review fixes; CI scope and ownership updated`.

## P7 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit, architecture and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(iam051): main merged, conflicts resolved`.

## P8 — The demo server (P-13, P-14)

Record every server change and its undo command **as you make it**.

1. Check P-14's condition.
2. Take the backup, then **verify** it. Take the read-only before-picture.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`. If the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident). Record the downtime and the time to all healthy.
4. **Then check**, with evidence in `artifacts/plan51/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcIam51VpsChecks` class, and each one cleans up what it leaves open.
   1. **Self-service:**
      - as a demo user, POST `/ui/ess/Shifts/Create` and GET `/ui/ess/api/internal/employees/{id}/lifecycle-flags` answer 404, and the gateway's audit records `Denied:EssNotExposed`;
      - GET `/ui/ess/EssPortal` reaches the self-service policy (403 for a user without `hr-employee`).
   2. **The audit:** `e2e-hr-admin` gets 403 on `/Home/Audit`; `e2e-compliance` gets 200, with rows of its own agency only.
   3. **Onboarding:** `hradmin` gets 403 on `api/hr-core/api/onboarding/agencies/apply?dryRun=true`, and `apply-file` answers 404.
   4. **D-IAM-4 in demo mode:** the running configuration shows no exclusion. Show it without posting a payroll.
   5. **Writes and service calls:**
      - `e2e-checker-mgmt` still writes in HR.Core, and `e2e-maker-contracts` in HR.Contracts;
      - the PLAN-42..50 server checks, which drive service calls (custody starts, code generation, policy queries, lookups, delegations), still pass.
   6. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   7. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..50 server checks. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-14 says, prove the demo works, and record it.

Commit: `test(iam051): demo server on explicit policies (self-service closed, the audit for its owners, onboarding for the platform), PLAN-36..50 server checks still green`.

## P9 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/GATEWAY_RUNBOOK.md`: self-service and the audit;
   - `docs/SERVICE_TRUST_INVENTORY.md`: where `hr-core.workflow` is accepted;
   - `docs/ESS_001_SERVICE_CENTER.md`;
   - `docs/HIRE_RBAC_MATRIX.md`: the general ceiling;
   - `docs/SEED_001_MASTER_DATA.md`: no `apply-file`;
   - `docs/CI_AND_TESTING.md`: the architecture project and its lists;
   - `OPS_VPS_SINGLE_HOST.md`: what changed for the demo users, the backup location, the change table, the rollback note;
   - the «الحالة» lines for G-IAM-05, G-IAM-06, G-IAM-08 and G-IAM-09;
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter;
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD (with `-SkipTestcontainers` when the VPN is on);
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

   All must be green. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan51-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations. No deviation defers part of the plan.
   4. **The writes, before and after:** per service, the writes without an explicit policy, the ceiling list's size, and who reaches what.
   5. **The narrowed paths and policies:** `/ui/ess`, `/Ess`, the directory, internal routes, the audit, `hr-core.workflow`, onboarding, evidence ingest — each line with the test that proves it.
   6. The guards and their own negative tests.
   7. Unit, architecture, integration and acceptance results; the PR run's artifact link as the evidence of record.
   8. **The server:** before and after, changes and their undo, the backup location, the rollback note.
   9. Items that other plans own.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times, and what the architecture project adds to the build;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 51`.
   12. **A readiness note:**
       - for PLAN-52: the branch scope on top of explicit policies;
       - for PLAN-53: the frozen Payroll list it must empty;
       - for wave 2: the pinned entity bindings (IAM-F), `hr-admin` (IAM-G), learning self-service (IAM-H).
4. **Merge (P-15), by you:**
   1. Merge the feature branch into a new `release/plan-51` from `main` (`--no-ff`).
   2. Write `reports/plan51-pr.md` (short).
   3. `git push -u origin release/plan-51`
   4. `gh pr create --base main --head release/plan-51 --title "PLAN-51: an explicit policy for every action, and no wide paths or wide policies left" --body-file reports/plan51-pr.md`
   5. Wait for the checks in the session («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then:
   - `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`;
   - record the PR link, each check result, the merge commit and the post-merge run in `reports/plan51-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`;
   - commit that note on a local branch `docs/plan-51-merge-note`, as the plans before did, so the next plan brings it in. **Do not open a second PR for it;**
   - stop the keep-awake process (P-2).

## Definition of done

- **Self-service:** no partial `/Ess` match in the gateway, and `/ui/ess` passes only the named controllers and the static files.
- **Service endpoints and the directory:** no `api/internal` route accepts an interactive user, and `HrCore.Lookup` admits staff and services only.
- **The gateway's audit** has its policy and its agency filter.
- **The filter:** no name list, and every write without an explicit policy meets its module's write policy in the service.
- **`HRManagement.ArchitectureTests` is green:**
  - every write-policy list is empty, except Payroll's (PLAN-53);
  - the sensitive-actions list holds only Payroll rows that PLAN-53 owns;
  - the entity-binding list holds nothing from HR.Movements, and elsewhere only wave-2 rows, each with its reason.
- **Nothing in the plan was deferred to save time.**
- **Service accounts and onboarding:** `hr-core.workflow` is accepted on the three endpoints only, `Onboarding` is for `platform-admin`, and `apply-file` is gone.
- **The tests:** AC-IAM-51a..f = Pass (the PR run's artifact is the evidence of record), and AC-07, AC-HCM-25/28/31, AC-IAM-50 and the PLAN-36..50 suites are green.
- **The server** runs this code.
- **The documents** P9 lists are updated.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server, and what the demo users now see differently;
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
