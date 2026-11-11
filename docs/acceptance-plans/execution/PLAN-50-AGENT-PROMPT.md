# Task: PLAN-50 — The management approver reaches his approvals, and delegation has a real effect (IAM-A)

The deliverables:
- **The approver reaches his approvals.** `hr-management-approver` alone, without `hr-movements`:
  - opens the list and the details of transfers, secondments, loans, promotions and assignments, and approves them through the gateway;
  - reaches the HR.Core actions under `HrCore.Approve`, and the delegation page;
  - creates, edits, executes and cancels nothing.
- **The creator role does not approve.** `hr-movements` creates and executes; it never approves, and nobody approves a request he created.
- **Delegation has a real effect** in the workflow engine (HR.Core) and in movement approvals (HR.Movements):
  - a delegate sees his principal's items, and approves the delegated role's step inside the period and the scope;
  - he is refused outside them, and after a revocation;
  - «on behalf of» is recorded in the step history, in the signature and in the movement's approval record.
- **Delegation never passes separation of duties:** a delegate approves nothing that he or his principal created, and a delegated role cannot be delegated again.
- **One delegation entity:** `WorkflowDelegation` is gone, and no delegation exists without a role scope.
- **The demo server** runs it.

It runs in one session with no owner action: the owner asked the agent to merge, so you merge after green checks.

## How to run this task — read before anything else

This is a **single prompt for one session**. Run it as phases **P0–P8** in order. **There are no owner actions in this plan**: every decision is taken (see «Decisions»).

**One plan at a time** (owner, 2026-11-07): this session works alone, in the main folder `D:\my projects\HRManagement`. There is no second clone and there are no locks.

**Standing owner instruction on merges** (PLAN-38 to PLAN-49 and PLAN-66): **you** push the release branch, open the PR, wait for the checks, and merge — but **only when every check is green** (P-15).

If something truly needs the owner (a Stop condition), write `BLOCKED:` and end the turn.

**At the end of every phase:**
1. Build: `dotnet build HRManagement.slnx -warnaserror`, 0 errors.
2. Run the phase's tests.
3. Commit, using the phase's commit message.
4. Update **`reports/plan50-progress.md`** — your checkpoint. It contains:
   - phase status;
   - decisions (with reasons);
   - files changed;
   - test results;
   - server changes and their undo commands;
   - open issues;
   - the exact next step.

**If you are resuming:** at start, or after any context compaction or restart, read `reports/plan50-progress.md` and `git log --oneline -20` **first**. Continue from the recorded next step. Never redo a committed phase. Before any remote or server step, check `origin/main` and the server's running tag.

**Timing (mandatory, the owner compares plans):**
- The **very first action** of the session is to create `reports/plan50-progress.md`. Its first line is:
  `Started (UTC): <timestamp>`
  where the timestamp comes from `(Get-Date).ToUniversalTime().ToString('o')`.
- Every phase records `Finished (UTC):`.
- Every resume records `Resumed (UTC):` with the reason.
- Record the duration of the inventories, image builds, stack starts, acceptance runs, the server build and deploy, and every GitHub Actions run.
- Record the time spent waiting for CI, and **any time the session stood still** (a stopped turn, the usage limit, the machine asleep), each with its cause.
- Never edit an earlier stamp.

**Economy mode** (the owner runs this session in the Claude desktop app, at `xhigh`). PLAN-49 stopped twice at the usage limit of the owner's plan (7 h 16 min), so spend with care:
- Use **at most two subagents at a time**. Good uses: the P0 inventories, read-only; root-causing separate failures.
- Run **one independent review**, before the server phase, so that the single server build carries its fixes. Use a refuter only for findings rated high. PLAN-49 ran three rounds (8, 12 and 35 findings, none high): do not repeat that.
- If the owner switches the session's multi-agent mode on, use it for one read-only verification of the review's fixes, two agents at a time.
- Write long outputs (inventories, logs, trx files) under `artifacts/plan50/`. Read only the failing parts.
- Do not re-read large documents you have already read in this session; take notes in the progress file instead.
- Build the acceptance images once per code state. Run the unit and integration filters before a stack run, and rerun only the classes that failed.

**Long commands.** A foreground command that outlives the tool's time limit ends the turn: PLAN-49 lost 2 h 51 min that way. So:
- Run anything that can outlive the limit in the background or detached, with its log under `artifacts/plan50/`, and poll it. That covers the unit suite, the Testcontainers step, an image build, a stack start, an acceptance chain, `ci-local.ps1` and the server build.
- Run **one heavy job at a time**: never an image build beside a host test run (P-2).

**Waiting — for CI and for your own work.**
- **Never end the turn to wait** for CI or for work you started (a review, a build, a long check). Wait for it in the turn, as PLAN-49 did.
- **For CI:** run `gh pr checks <n> --watch --interval 60` in the foreground, with the tool's longest time limit. When it returns because of the limit, run it again, until every check has finished. A background `gh run watch <id> --exit-status` is fine too, if this session's background commands wake it when they end.
- **Then act at once:**
  - every check green → merge (P-15);
  - a red check → read its log as soon as it fails, without waiting for the other checks; fix it on the release branch, push that branch only, and wait again the same way.
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
- separation of duties and delegation of authority, without ever putting a delegated role into a token;
- approval workflow engines: step roles, entity gates, step history, signatures;
- EF Core, PostgreSQL row level security (`FORCE`, non-owner roles) and migrations;
- service-to-service calls (client credentials, scopes, tenant headers) and Keycloak (attributes, mappers, the user profile);
- Docker Compose, PowerShell 5.1 and bash.

You work on the `HRManagement` repository on the owner's Windows machine (PowerShell **5.1 only, no `pwsh`** unless the owner installs it, Docker Desktop, culture ar-SA, Windows OpenSSH, `gh` signed in as `omardefdev`). You also operate the demo server through `ssh hrm-vps`, as PLAN-36..49 did.

**Where things stand** when this prompt was written (2026-11-11):
- **`main`** = `1ee60c47`. It holds PLAN-49 (PR 47).
- **PLAN-49's merge note** is a single commit, `2570ba04`, on the local branch `docs/plan-49-merge-note`, not yet on `main`. The main folder is checked out on that branch.
- **The demo server** runs `046316bde9e7`, which is the product code of `main`: only tests, documents and reports changed after it.
- **The side PRs** about the E2E Playwright install (#40, #42), if still open: never touch them. `main` is strict, so if one merges before your PR, you merge `main` and wait for a rerun.
- **CI today:**
  - a PR run has 16 checks and takes about 45 minutes; one acceptance stage can be rerun alone (`gh run rerun <id> --job <id>`);
  - the stage jobs are `lab-main`, `lab-rest`, `prodlike`, `prodlike-policy`, `prodlike-final`, `leastpriv` and `meta`;
  - a commit that leaves the code tree unchanged reuses the PR's green run;
  - `acceptance-job.ps1` takes `-Classes` and `-Stage`;
  - a new class goes into `lab[]` or `prodLike[]` of `tests/acceptance/ci-scope.json`, in a group a stage job runs;
  - `Qa066WorkflowTests` forbids a later stage on a stack that an overlay changed.
- **From PLAN-49** (its final report, §3, §6, §10 and §11):
  - **A class that fails in the full Testcontainers step and passes alone is a real failure.** The PostgreSQL server of a collection is shared, and CI runs the whole step. `PostgreSqlFixture` now gives connection strings whose idle connections close after seconds; a new PostgreSQL test uses them (`docs/CI_AND_TESTING.md` has the two rules).
  - **`scripts/acceptance-verify.ps1` needs PowerShell 7,** which this machine does not have. PLAN-49 compared its 32 seeded counts by psql; CI runs the script.
  - **The disk:** Docker Desktop keeps its data disk on drive C, which filled to zero during PLAN-49 and killed the Docker engine. A failed image build then left stale images, and the `-NoBuild` stages after it proved nothing (P-2).
  - **A cold image build over the owner's network** can fail in `dotnet restore`. PLAN-49's way round it (the NuGet cache filled from the host) is in its progress file.
  - **Known and not this plan's:**
    - the server check `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist` fails in every full run of the server suites and passes alone after a sweep cycle (ten minutes): rerun it alone, do not chase it;
    - `Ac09ShadowPolicyTests` is blocked by design since PLAN-42 (PLAN-65 rewrites it);
    - `AcWf46PolicyOrphanTests` runs with hr-core-web out of Enforce;
    - the four payroll classes marked not required fail in every green run.
  - **Files PLAN-49 shares with this plan:** `EstablishmentChangesController.cs`, `docker-compose.acceptance-prodlike.yml`, `deploy/wiring/service-wiring.json`, `tests/acceptance/ci-scope.json` and `ac-ownership.json`.
- **The code today** (read on `1ee60c47`). The plan's line numbers have moved: find the code by its content.
  - **The module filter:** `HrModuleAuthorizationFilter` applies the module policy to every action. It lets through `[AllowAnonymous]`, any action with a `Service.*` policy, and a list of controller names (`Ess*`, `HireWizard`, `LookupApi`, `Policies`, `PolicyTemplates`, `Onboarding`, `RecruitmentAppeals`, `WorkInbox`, `WorkflowApprovals`, `ExamMarking`, `WorkflowDefinitions` and others).
  - **HR.Movements:**
    - The whole service sits behind `Module.HrMovements` (Admin or `hr-movements`), in its filter and in the gateway.
    - `Approve` on the five types, and `Reject` on transfers, carry `HrManagement.Approve` (Admin or `hr-management-approver`). So today only an administrator can approve.
    - Every other action has no policy of its own.
    - The creator check compares `RequestedBy` with the actor, then calls a `SeparationOfDutiesService` built without delegations.
    - **A naming trap:** `DelegationRequestsController` and `DelegationRequest` are the movement type «تكليف». They are not approval delegation.
  - **The gateway** (`GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy`), for HR.Core:
    - GET → `HrCore.ReadOnly`; the inbox → `HrCoreWorkInbox`.
    - `/ui/hr-core/WorkflowApprovals` → `WorkflowStepAction` on POST, `HrCoreWorkInbox` on GET (PLAN-45).
    - `/api/workflow-instances/{id}/approve|reject` → `WorkflowStepAction`; the rest of that API → `WorkflowExternalStart`.
    - MVC `ApproveWorkflow`, `RejectWorkflow`, `ApproveResultsWorkflow`, `RejectResultsWorkflow` → `HrCore.BranchOperation`. They are matched with `Contains`.
    - `Approvals/Approve|Reject` → `HrCore.Approve` (PLAN-44); `/api/onboarding/` → `HrCore.Approve`.
    - The policy pages have their own rules (`TryGetPolicyLifecyclePolicy`, PLAN-47).
    - Every other POST → `HrCore.Write`.
  - **The policies:**
    - `HrCore.Approve` and `SoD.ManageDelegations`: Admin, `hr-core`, `hr-management-approver`, `platform-admin`.
    - `WorkflowStepAction`: no service account, and Admin, `hr-core`, `branch-manager`, `hr-management-approver`, `admin-logistics`, `idcard` or `compliance`.
    - `HrCore.BranchOperation`: no service account, and the first four of those.
  - **HR.Core actions under `HrCore.Approve`:**
    - `EmploymentRecords.Approve`;
    - `Competitions.Finalize` and `Cancel`;
    - `EmployeeCorrections.Approve` and `ApproveApi`, in a controller whose class carries `HrCore.Write`;
    - `Approvals.Approve` and `Reject`;
    - the three `Onboarding` API actions.
  - **MVC workflow actions under `HrCore.BranchOperation`:** in `RecruitmentRequisitions`, `OfferLetters`, `SelectionCommittees`, `EstablishmentChanges` and `Competitions` (two pairs).
  - **Delegation:**
    - `ApprovalDelegation` has `PrincipalId`, a required `DelegateId` and two optional scopes. Its table is a tenant table under forced row security.
    - Only `DelegationsController` reads it.
    - `ResolveDelegate` ignores `RoleScope` and has no caller.
    - `WorkflowDelegation` has an entity and a `DbSet`, and no reader.
  - **The engine:**
    - `WorkflowApprovalEvaluator` decides in this order: structure → step role → `WorkflowSodRules` (PLAN-43) → the entity gate → the initiator rule.
    - `WorkflowActionActor.FromPrincipal` carries the roles, the employee id and the org-scope claim.
    - `WorkflowStepHistoryEntry` has no «on behalf of» field.
    - `WorkflowSignatureService` signs a payload of twelve fields, and `SignatureVerifier` compares a record with a payload its caller rebuilds.
    - `HrCoreWorkInboxService.DelegateItemAsync` throws `NotSupportedException`.
  - **`employee_id`:**
    - `KeycloakUserProvisioningService` writes the attribute `employeeId`; the realm's mapper reads `employee_id`.
    - The manifest's user profile declares `employee_id`, but nothing brings a live realm's profile in line with the manifest. Keycloak silently drops an admin write of an attribute the profile does not declare (PLAN-45).
  - **Personas:** every persona the plan names exists.
    - `a-management-approver` holds `hr-management-approver` alone; `a-movements` holds `hr-movements`; `central-a2` and `central-a` hold `hr-core`; `a-supervisory-reader` holds `hr-core-supervisory`.
    - **Every persona and every demo user also carries Keycloak's `offline_access` role.**
    - The plan edits only the personas' `scenarios` lists: no realm change, and `DATA-AUTH` stays at 65.
  - **Demo users** carry `tenant_id` only, so no demo token has `employee_id`. `e2e-checker-mgmt` holds `hr-management-approver` and `hr-core`; `e2e-movements-ops` holds `hr-movements`.
  - **Wiring:** hr-movements has the client `svc-hr-movements`, the scope `hr-core.lookup` and one call (`AddHrCoreActiveRegimeClient`). `PolicyEnforceClient` shows how a service call carries the user's tenant (PLAN-42).
  - **Tests that touch this plan:**
    - `AcInt37ProductionWiringTests` creates a transfer and a promotion as `a-movements`;
    - `AcMsg40AtomicPublishingTests` executes a promotion as `a-movements`;
    - `Ac06TransferAccessTests` (lab) covers transfer access.
- **Lessons for every plan:**
  - **A data change in a migration on a tenant table is invisible under forced row security** to `*_migrator` (PLAN-43). `WF044_WorkflowInstanceIntegrity` shows the way: lift `FORCE` on the tables it reads inside the migration's own transaction, then restore it.
  - **A job or a start-up step that reads several tenants** must walk them from a source outside row security and set `TenantIdFlow` for each (PLAN-44, PLAN-45, PLAN-48). `IgnoreTenantFilterForSystem` lifts only the EF filter, never row security.
  - **Testcontainers connects as a superuser,** so it cannot show a row-security defect. Prove such things as a non-superuser role (PLAN-43, PLAN-44, PLAN-47, PLAN-48, PLAN-49).
  - **Who connects how:** the demo server and the default acceptance stacks connect HR.Core as the PostgreSQL superuser; production and the least-privilege stack connect as `hr_core_app`.
  - **The prod-like stack runs `ASPNETCORE_ENVIRONMENT=Acceptance`,** so it does not load `appsettings.Production.json`. A value in `appsettings.json` loads everywhere; a value in a Production file must be mirrored in `docker-compose.acceptance-prodlike.yml`.
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
    - the server suites write into the committed `acceptance-evidence/` and `reports/`: restore those files, or move them under `artifacts/plan50/vps/` (PLAN-48);
    - a server check cleans up what it leaves open, and decodes a page before it compares Arabic text (PLAN-49).
  - **Before the push:**
    - compare the seeded counts of `scripts/acceptance-verify.ps1` on your local stack, by psql (PLAN-48, PLAN-49);
    - run the full Testcontainers step on the host with CI's filter, when `ci-local.ps1` runs with `-SkipTestcontainers` (PLAN-49);
    - a log line that writes a value built from the request is CodeQL's `cs/log-forging` (PLAN-48);
    - a test literal that looks like a key trips gitleaks' `generic-api-key`. Write it differently rather than allowlisting it, and never quote a flagged value in a report (PLAN-48).
  - **When the owner's VPN is on,** ci-local's Testcontainers fail with "Failed to connect to 192.168.65.254". Run those classes on the host, then `ci-local.ps1 -SkipTestcontainers`. That is allowed, because Testcontainers run on ubuntu-latest in CI (PLAN-48).
- **Practical notes:**
  - Bash heredocs containing Arabic fail in this environment: edit Arabic text with the Edit or Write tools.
  - The acceptance services have no container health check: readiness means `/health/ready`.

## Read first (mandatory)

1. `docs/acceptance-plans/PLAN-50-iam-approver-reach-delegation.md` — **the plan; it is the specification.** Where this prompt deviates from it, this prompt wins, and the deviation goes into the final report.
2. These reports:
   - `reports/plan49-final-report.md`, in full;
   - the authorization parts of `reports/plan43-final-report.md` (`WorkflowStepAction`, the SoD rules, the step-role exemptions) and `reports/plan45-final-report.md` (`WorkflowApprovals`, the demo users);
   - the wiring parts of `reports/plan37-final-report.md` and `reports/plan42-final-report.md` (service calls, internal routes, the tenant of a service call).
3. These documents:
   - `docs/IAM_001_SCOPE_GAP_STUDY.md` (G-IAM-01), `docs/FUN_003_MOVEMENTS_GAP_STUDY.md` (G-MOV-01), `docs/WF_001_GAP_STUDY.md` (G-WF-11);
   - `docs/SOD_MATRIX.md`, `docs/HIRE_RBAC_MATRIX.md`, `docs/WF_001_WORKFLOW_ENGINE.md`, `docs/WF_002_WORK_INBOX.md`.
4. The code named in «The code today».
5. `docs/CI_AND_TESTING.md` (the local policy) and `docs/OPS_VPS_SINGLE_HOST.md`.
6. `docs/OWNER_DECISIONS_REGISTER.md`: D-IAM-1, D-MOV-2, D-WF-5, and D-50-1 to D-50-5.
7. Every file the plan cites.

## Preconditions (check in P0; `BLOCKED:` if any fails)

1. **You are in `D:\my projects\HRManagement`,** on `main` or on `docs/plan-49-merge-note` (where PLAN-49 left the folder), and no other plan session is running.
2. **`git fetch origin`**, and `origin/main` holds PLAN-49 (`1ee60c47` or later).
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

- **D-IAM-1 and D-MOV-2, who approves movements:** `hr-management-approver` reads the list and the details, and approves and rejects, nothing else. Creating, editing, executing and cancelling belong to `hr-movements`. The approver's list shows what awaits approval by default.
- **D-WF-5, the delegation policy:**
  - at most 90 days (`SodMatrixDefaults.MaxDelegationDays`);
  - `RoleScope` is mandatory, and must be a role the principal holds **directly** in his token when he creates the delegation;
  - `EntityTypeScope` is optional, from a closed list (`Delegation:EntityTypes`);
  - no chained delegation;
  - a delegate approves nothing that he or his principal created.
- **D-50-1, delegable roles:** `Delegation:DelegableRoles = ["hr-management-approver"]`. Any other role is refused at creation, with an Arabic message.
- **D-50-2, who the delegate is:** an employee chosen from HR.Core's directory. `DelegateEmployeeId` is stored and matched with the `employee_id` claim. Old rows that hold only `DelegateId` are matched with `sub`.
- **D-50-3, the delegate's reach:**
  - In HR.Movements, the four approval screens (list, details, approve, reject) accept an active delegate, even with no module role.
  - In HR.Core, the delegate must hold an HR.Core read role to open the details and the inbox.
  - The gateway does not look up delegations. It lets every interactive staff user through the named approval paths, and the service decides.
- **D-50-4, existing delegations:** the migration revokes every row with no `RoleScope`, or with a role that is not delegable (`IsRevoked=true`, `RevokedReason="PLAN-50 migration"`).
- **D-50-5, the delay of a revocation:** a 60-second cache per user in HR.Movements (`Delegation:CacheSeconds`). Any lookup error means no delegation. A revocation acts at once in HR.Core, and within a minute in movements.

## Prompt-level decisions

| # | Decision |
|---|----------|
| P-1 | **Where and how.**<br>• Branch `feature/plan-50-iam-approver-reach-delegation` from `origin/main`, in the main folder.<br>• **The first commits:**<br>&nbsp;&nbsp;– cherry-pick PLAN-49's merge note from `docs/plan-49-merge-note` (`2570ba04`);<br>&nbsp;&nbsp;– commit this prompt file;<br>&nbsp;&nbsp;– then delete the merge-note branches whose content is on your branch or on `main` (`git branch -D`, after checking).<br>• **One plan at a time:** no locks and no second clone. |
| P-2 | **The machine: Docker, the disk, the ports.**<br>• Bring every stack you start down with `down -v` when you are done with it.<br>• Before a Docker-heavy run, check `docker ps`. Remove only this repository's leftovers: compose projects `hrm-*`, Testcontainers, and untagged images of this repository's own builds. Never prune volumes or images of other projects.<br>• **The disk:** Docker's data disk is on drive C. Before every image build and every stack start, read C's free space.<br>&nbsp;&nbsp;– Below 15 GB, remove this repository's leftovers first.<br>&nbsp;&nbsp;– If it is still below 15 GB, moving Docker's disk is a setting of the owner: `BLOCKED:`.<br>• **One heavy job at a time:** never an image build beside a host test run.<br>• **After an image build,** check that every service image was rebuilt from this tree. A failed build leaves stale images, and a `-NoBuild` run on them proves nothing.<br>• **Before each stack start,** compare the stack's host ports with `netsh interface ipv4 show excludedportrange protocol=tcp`. If a port the tests read is reserved, freeing it is a system change for the owner: `BLOCKED:` with the range. |
| P-3 | **The override attribute, and the approver's reach in movements (plan §1, §2).**<br>• **The attribute:** `ModulePolicyOverrideAttribute(policy)` replaces the module policy for one action or controller; the action's own `[Authorize]` still applies on top.<br>&nbsp;&nbsp;– A policy name that is not registered fails the boot.<br>&nbsp;&nbsp;– It is checked before the name list, which stays as it is (PLAN-51 replaces it).<br>&nbsp;&nbsp;– In this plan it never sits on a create, edit, execute or cancel action. A test enforces that.<br>• **The policies:** `HrMovements.ApprovalAccess` (Admin, `hr-movements`, `hr-management-approver`, or an active delegate of `hr-management-approver`) and `HrMovements.Write` (Admin, `hr-movements`).<br>• **The five controllers:**<br>&nbsp;&nbsp;– `Index`, `Details` and `Approve` (and `Reject` on transfers) carry the override with `HrMovements.ApprovalAccess`;<br>&nbsp;&nbsp;– `Approve` and `Reject` keep `HrManagement.Approve` on top;<br>&nbsp;&nbsp;– every other action carries `HrMovements.Write` explicitly, both the GET and the POST of a form.<br>• **The gateway:** match the two path segments after the module prefix exactly (the controller, then the action; the controller's root is `Index`), never with `Contains`.<br>• **The views:** the approve button shows only to someone who may approve, and the create, execute and cancel buttons only under `HrMovements.Write`. A hidden button is not the control: the 403 is.<br>• **What stays:** the creator checks, the state rules, and `EnsureTransferInScopeAsync` on every transfer action. |
| P-4 | **The gateway table, and `Gateway.ApprovalReach` (plan §3, §4).**<br>• **One table:** `GatewayHrCoreActionPolicies` is the only source of HR.Core's gateway exceptions for approval actions. `TryGetFineGrainedPolicy` asks it before `HrCore.Write`.<br>&nbsp;&nbsp;– Rows: the seven `HrCore.Approve` actions, `Delegations/Create` and `Delegations/Revoke` (→ `SoD.ManageDelegations`), and the two publish actions (→ `PolicyApprover`).<br>&nbsp;&nbsp;– Rules that exist today move in unchanged: `Approvals` (PLAN-44), the two publish rules of PLAN-47, and Onboarding's three API actions. The rest of `TryGetPolicyLifecyclePolicy` stays where it is.<br>&nbsp;&nbsp;– A row matches the conventional path `/ui/hr-core/{Controller}/{Action}[/{id}]` and the explicit API path, nothing wider.<br>• **`EmployeeCorrectionsController`:** the class carries `HrCore.ReadOnly`, the two request actions `HrCore.Write`, and the two approve actions `HrCore.Approve` alone.<br>• **`Gateway.ApprovalReach` counts only the platform's own roles.**<br>&nbsp;&nbsp;– It admits an interactive user with a valid `tenant_id` and at least one role of `HrManagementRoles` that is not in `Authorization:NonStaffRoles` (`hr-employee`, `hr-exam-marker`, `applicant`).<br>&nbsp;&nbsp;– Keycloak's own roles never count: `offline_access` (every persona and demo user carries it), `uma_authorization`, `default-roles-*`.<br>&nbsp;&nbsp;– A test proves that a token with `hr-employee` and `offline_access` is refused.<br>• **It opens only the plan's paths:** the movement approval paths of P-3 with that module's static files, the four MVC workflow actions of HR.Core, `/ui/hr-core/WorkflowApprovals/*` on POST, and `/api/workflow-instances/{id}/approve|reject`. Match the workflow actions by path segment too.<br>• **The guard:** `GatewayApproverReachTests`, as the plan says. It also fails when an action of an opened controller has no explicit policy in the service. |
| P-5 | **The delegation entity, its creation, and the lookup (plan §5).**<br>• **The fields:** the plan's, plus `PrincipalScopeOrgUnitId`: the principal's org-scope claim when he creates the delegation, null for a central principal (P-6).<br>• **The migration `HCM050_ApprovalDelegationScope`:**<br>&nbsp;&nbsp;– The revocation of D-50-4 is a data change on a tenant table under forced row security. Follow `WF044_WorkflowInstanceIntegrity`: lift `FORCE` inside the migration's transaction, then restore it.<br>&nbsp;&nbsp;– `Iam050DelegationPostgreSqlTests` runs it as the non-superuser migrator role with `FORCE` on, over old rows of two tenants.<br>&nbsp;&nbsp;– It drops `WorkflowDelegations` with its entity and its configuration. P0 counts that table's rows, on the acceptance data and on the server.<br>&nbsp;&nbsp;– No byte order mark; `has-pending-model-changes` clean.<br>• **Creation:**<br>&nbsp;&nbsp;– The delegate is picked from the employee picker.<br>&nbsp;&nbsp;– The role is picked from the roles the user holds directly that are delegable.<br>&nbsp;&nbsp;– `ValidateDelegation` refuses: a role outside the list or not held; an entity type outside the list; a delegate who is the principal, by user or by employee; more than 90 days.<br>&nbsp;&nbsp;– Each refusal has an Arabic message, and every creation and revocation writes an `EntityAuditLog` row.<br>• **No chain, and no role in the token:** a delegation never appears in `IsInRole`. Its effect exists only at the decision points of P-6 and P-7.<br>• **The matcher:** `ApprovalDelegationMatcher` is a pure function. `ResolveDelegate` and `IsActingAsDelegate` are rebuilt on it.<br>• **The lookup:**<br>&nbsp;&nbsp;– `DbApprovalDelegationLookup` in HR.Core reads the database, with no cache.<br>&nbsp;&nbsp;– The internal endpoint `GET api/internal/approval-delegations/active` carries `Service.ApprovalDelegationRead`: a service account with `hr-core.lookup` whose client is in a list (`svc-hr-movements`). An interactive user gets 403.<br>&nbsp;&nbsp;– The call carries the user's tenant the way `PolicyEnforceClient` does, and the endpoint reads that tenant only.<br>&nbsp;&nbsp;– `HttpApprovalDelegationLookup` caches per tenant, user and employee (D-50-5), and any error means no delegation.<br>&nbsp;&nbsp;– Add its `calls[]` entry to `deploy/wiring/service-wiring.json`; `ProductionWiringMatrixTests` and `S2s042InternalRoutesAreServiceOnlyTests` stay green.<br>• **`RoleOrDelegationRequirement`:** with no lookup registered, it is roles only. That is the case in the gateway and in HR.Contracts (`HireWizard.Step3Approve`); a test proves it.<br>• **`employee_id` for provisioned users:**<br>&nbsp;&nbsp;– Fix the attribute name in `KeycloakUserProvisioningService`, with a unit test on the payload.<br>&nbsp;&nbsp;– Read the live demo realm's user profile in P0. If it does not declare `employee_id`, the fix has no effect there.<br>&nbsp;&nbsp;– Then make the reconcile step declare the manifest's attribute (admin edit only, idempotent). If the reconcile cannot own the profile safely, record it as a precondition of PLAN-62. |
| P-6 | **The effect in the workflow engine (plan §6).**<br>• **The order does not change:** structure → step role (direct, then delegated) → `WorkflowSodRules` → the entity gate → the initiator rule.<br>• **With a delegation, every identity rule after the role runs twice:** as the delegate, and as the principal. That is the SoD rules of PLAN-43, the entity gate and the initiator rule. Any refusal wins. A reject follows the same rules.<br>• **A delegate never gets a wider org scope than his principal had:** the pass as the principal uses the scope stored on the delegation (P-5). A test proves that the delegate of a branch-scoped principal cannot approve outside that branch.<br>• **The direct role wins:** an actor who holds the step's role himself acts in his own name, and `OnBehalfOf` stays null.<br>• **The history:** the three new fields of `WorkflowStepHistoryEntry` are optional. Old history JSON still reads, and a direct action writes none of them.<br>• **The signature:**<br>&nbsp;&nbsp;– A direct action signs the payload it signs today, so an existing signature still verifies.<br>&nbsp;&nbsp;– An action on behalf adds `onBehalfOfUserId` and `delegationId`, and its `SignerRole` is the delegated role.<br>• **The route policy:** `WorkflowStepAction` becomes roles or delegation, still with no service account. It also replaces `HrCore.BranchOperation` on the MVC workflow actions of the five controllers. There is no new parallel policy; the evaluator is the fine decision.<br>• **The inbox and the panels** build the actor with his delegations, show «بالنيابة عن {الأصيل}», and compute `CanApprove` with the same evaluator.<br>• `DelegateItemAsync` stays unsupported, with a message that points to the delegation page.<br>• Entity types in `Workflow:StepRole:ExemptEntityTypes` are untouched. |
| P-7 | **The effect in movement approvals (plan §7).**<br>• Register `IApprovalDelegationLookup` (the HTTP one) and `IApprovalActingContext` in HR.Movements.<br>• `[ApprovalEntityType]` on `Approve` and `Reject` of the five controllers, with the plan's five names.<br>• **Separation of duties:** refuse when `RequestedBy` is the delegate or the principal, with «لا يجوز اعتماد طلب أنشأه المعتمد أو أصيله».<br>• **Scope:** where a scope check exists today (transfers), the principal's stored scope applies as well as the delegate's own.<br>• **«On behalf of»:** `OnBehalfOfUserId` and `OnBehalfOfName` on `TransferApproval` and `MovementChecklist`. `MOV050_ApprovalOnBehalfOf` only adds the columns.<br>• **Fail-closed:** a failed lookup never stops a holder of the direct role, and always stops a delegate.<br>• **The cache works in both directions:** a revocation, and also a new delegation, can take up to `Delegation:CacheSeconds` to act in movements. The acceptance test waits for it instead of lowering the value. |
| P-8 | **Row security.**<br>• Production connects HR.Core as `hr_core_app` under forced row security. Both lookups read `ApprovalDelegations` in the acting user's tenant.<br>• Prove, as a non-owner role without `BYPASSRLS`, in an integration test (the PLAN-49 route):<br>&nbsp;&nbsp;– the in-process lookup and the internal endpoint return a delegation of the caller's tenant;<br>&nbsp;&nbsp;– they return nothing for another tenant, and nothing when the call names no tenant.<br>• Add a class to the `leastpriv` stage only if it can carry one more. Record which route you took. |
| P-9 | **Existing tests that the new rules change.**<br>• A test that relied on an old gateway rule, on `HrCore.BranchOperation` for a workflow action, or on a required `DelegateId` changes to the new rule. Never weaken an assertion.<br>• **`GatewayPathAuthorizationMiddlewareTests`** is updated for the moved rules; every refusal it proved before, it still proves.<br>• **AC-HCM-25's assertions and `Hcm025PanelServerParityTests` do not change:** the panels and the server still ask the same evaluator.<br>• **AC-POL-47 and AC-TEN-48** stay green on the policy pages whose gateway rules moved into the table.<br>• **`AcInt37` and `AcMsg40`** still create and execute as `a-movements`. |
| P-10 | **Acceptance placement (PLAN-41 and PLAN-66 rules).**<br>• `AcIam50ApproverReachDelegationTests` (AC-IAM-50a..e): `prodLike[]`, `required: true`, group `main`. It must not stop or recreate containers.<br>• `ac-ownership.json`: `AC-IAM-50` (owner PLAN-50, `provesClaim: true`).<br>• **The delegate** in the scenarios is `central-a2`, as the plan says.<br>• **The evidence of record is the PR run's artifact,** not `acceptance-evidence/AC-IAM-50/` as the plan says. Copy what the report needs into `artifacts/plan50/`, then restore the committed evidence. |
| P-11 | **Local testing (owner's rule of 2026-11-06).** Run only the classes this plan touches, with `scripts/ci/acceptance-job.ps1 -Profile <ProdLike or Lab> -Classes <list>`. **The full suite runs once, in CI.** The list, at least:<br>• **prod-like:** `AcIam50ApproverReachDelegationTests`, `AcHcm25ProductionApprovalTests`, `AcWf43DefinitionGovernanceTests`, `AcWf44InstanceIntegrityTests`, `AcWf45AssetApprovalLoopTests`, `AcWf46CrossServiceApprovalTests`, `AcPol47RuleLifecycleTests`, `AcTen48NationalScopeTests`, `AcPol49EngineScopeTests`, `AcInt37ProductionWiringTests`, `AcMsg40AtomicPublishingTests`, and the prod-like journey;<br>• **lab:** `Ac06TransferAccessTests`, `AcEst24EstablishmentWorkflowUiTests`, `AcHcm21RequisitionApprovalUiTests`, `AcHcm22RecruitmentWorkflowsUiTests`, `AcHcm28CommitteeAppealTests`, `AcHcm31CompetitionTests`, `AcWf23WorkInboxUiTests`, the lab `isolated` group (`AcHcm26`, `AcHcm27`), and the lab journey;<br>• **leastpriv:** `AcTen48LeastPrivilegeTests`, and P-8's class if you add it;<br>• and any class that P0 finds posting an approval through the gateway or reading a delegation.<br>Then compare the seeded counts of `scripts/acceptance-verify.ps1` by psql. Known local-only failures of lab classes that need PowerShell 7 (PLAN-43 §6) are recorded, not chased. |
| P-12 | **PowerShell 5.1.**<br>• Every new or changed `.ps1` is ASCII-only and runs under `powershell -NoProfile -ExecutionPolicy Bypass -File …`. Replace every `pwsh` in the plan's Verification block.<br>• `scripts/iam050-validate.ps1` holds no Arabic literal. Decide what it checks and say so in the report; at least: every approval action has its gateway rule, the configuration lists are valid, and the internal route is service-only.<br>• Start stacks with `scripts/acceptance-up.ps1 -Profile ProdLike`, not with the plan's raw `docker compose` lines. |
| P-13 | **The demo server facts this plan must respect.**<br>• It connects HR.Core as the superuser, so row security is not enforced there. P-8's proof lives in the tests.<br>• **No demo token carries `employee_id`,** and D-50-2 matches a delegate by it. For the server check, and for the owner's own trials:<br>&nbsp;&nbsp;– add one demo user with `hr-core` and no approver role, through `scripts/vps/add-demo-user.sh` extended with an optional employee id;<br>&nbsp;&nbsp;– link it to a demo employee that exists on the server;<br>&nbsp;&nbsp;– record it in the manifest, and in the runbook with its undo.<br>&nbsp;&nbsp;The password is the demo users' password, and it is never printed.<br>• `e2e-checker-mgmt` is the principal and the approver; `e2e-movements-ops` is the creator.<br>• **P0 lists, read-only:**<br>&nbsp;&nbsp;– the rows of `ApprovalDelegations` (how many D-50-4 will revoke) and of `WorkflowDelegations`;<br>&nbsp;&nbsp;– the live realm's user profile;<br>&nbsp;&nbsp;– the scope of `svc-hr-movements`. |
| P-14 | **The demo server.**<br>• **When:** only with the running tag (`/opt/hrm/current-tag`) on `origin/main` (`git branch -r --contains <tag>`).<br>• **Backup first:** `pg_dumpall` under `/opt/hrm/backups/pre-plan50-<utc>/` (700/600), verified as before (all `CREATE DATABASE` statements, `SHA256SUMS`).<br>• **Before the deploy, read-only:** P-13's picture.<br>• **Build once,** on the final code, detached; then `scripts/vps/deploy.ps1 -Tag <sha12>`.<br>• **Rollback safety:** find out what PLAN-49's tag (`046316bde9e7`) does with this plan's data: a nullable `DelegateId`, the new columns, a dropped `WorkflowDelegations` table, the revoked rows. Record it in `OPS_VPS_SINGLE_HOST.md`.<br>• **The demo stays usable** at the end of every turn. If a step fails and cannot be fixed in the turn:<br>&nbsp;&nbsp;– roll back to PLAN-49's tag, or fix forward;<br>&nbsp;&nbsp;– restore the backup only if data was damaged;<br>&nbsp;&nbsp;– prove the demo works, and record the downtime. |
| P-15 | **Merge, by you, only after green checks.**<br>1. Push the release branch **once**, in P8, after local CI and the pre-push check are green.<br>2. `gh pr create`, then wait in the session («Waiting»).<br>3. Only when every check is green: `gh pr merge <n> --merge`.<br>4. A red check: fix it on the release branch, push that branch only, and wait again. For a stage that failed plainly from the environment, `gh run rerun <id> --job <id>`, and record it.<br>5. `main` is strict: if another PR merges first, merge `origin/main` into the release branch and wait for the rerun.<br>**The evidence of record is the PR run's artifact:** link it in the merge note, and commit no evidence. Never push to `main` directly, never force-push. |
| P-16 | **No regression of PLAN-25, PLAN-36..49 and PLAN-66.** These stay green:<br>• AC-HCM-25a..h and `Hcm025PanelServerParityTests` (P-9);<br>• `AcWf43`..`AcWf46` with the orphan class, `AcPol47`, both `AcTen48` classes, both `AcPol49` classes, `AcS2s42`, `AcMsg40`, `AcInt37`, `AcOps38`, and both recruitment journeys;<br>• the `Wf043*`, `Wf044*`, `S2s042*`, `Pol047*`, `Ten048*` and `Pol049*` tests, `GatewayPathAuthorizationMiddlewareTests`, `ProductionWiringMatrixTests`, `S2s042InternalRoutesAreServiceOnlyTests`, `WorkflowDefinitionsHaveHandlersTests`, `Qa066*`;<br>• `scripts/wf043-validate.ps1`, `scripts/hcm025-validate.ps1`, `scripts/pol047-validate.ps1`, `scripts/pol049-validate.ps1`, `scripts/vps/lint-compose.ps1` (both chains), `scripts/int037-wiring-check.ps1`. |

## Scope boundaries

**Out of scope** (from the plan):
- **PLAN-51:**
  - replacing the filter's name list with the attribute in every service;
  - explicit write policies for the other movement controllers (notices, distribution plans, executions, financial impacts);
  - restricting `/ui/ess` to self-service paths.
- **PLAN-37 and PLAN-42, done earlier:** service addresses, scopes and secrets in production; the trust boundary between services.
- **PLAN-62:** turning the Keycloak synchronisation on in production. Only the attribute name is fixed here.
- **Wave 2:**
  - delegating other roles, and delegation in `HireWizard.Step3Approve` and in the legacy `Approvals`;
  - automatic absence, escalation and notification (WF-F);
  - revoking a leaver's delegations (G-SEP-17);
  - rejecting secondments, loans, promotions and assignments (their statuses have no `Rejected`);
  - «edit then approve» (G-MOV-11) and the branch scope of movements (G-MOV-10, PLAN-52).

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
- put a delegated role into a token, a claim or `IsInRole`, or give a user a Keycloak role to make a scenario pass;
- open a gateway path beyond those P-4 names;
- print a password, or rotate a secret that exists on the server;
- weaken a guard, lower a coverage floor, delete a CI step, exclude a class, or mark a class not required to get green;
- leave the demo server down at the end of a turn.

## Stop (record `BLOCKED:` and end the turn) if

- **A precondition fails.**
- **A named approval path cannot be opened at the gateway** without also opening an action that is not an approval.
- **A delegation can only take effect by changing a token or a Keycloak role.**
- **AC-HCM-25's assertions or `Hcm025PanelServerParityTests`** would need a change of meaning (P-9).
- **The lookup cannot read the acting user's tenant** under forced row security without new trust between services (P-8).
- **Drive C stays below 15 GB, or a host port the tests read is reserved** (P-2).
- **A server deploy fails,** and neither the rollback nor the restore brings the demo back.

---

## P0 — Timing, preconditions, baseline, inventories

1. Write the `Started (UTC)` line (see «Timing»). Check the Preconditions.
2. `git switch -c feature/plan-50-iam-approver-reach-delegation origin/main`. Bring in PLAN-49's merge note and commit this prompt file (P-1).
3. **The plan's baseline** («المرحلة 0») into `reports/plan50-baseline.md`:
   - **0.2 and 0.3:** the build, and the listed filter;
   - **0.4, the reach table:** every action whose policy is `HrManagement.Approve`, `HrCore.Approve` or `SoD.ManageDelegations`, in every assembly — its conventional path, its gateway policy today, and whether `hr-management-approver` alone reaches it;
   - **0.5, delegation:** every read of `ApprovalDelegations` and `WorkflowDelegations`, and every `new SeparationOfDutiesService(`;
   - **0.6:** what PLAN-37..49 changed in the filter, the gateway, the evaluator and `WorkflowInstanceActionService`.
4. **Inventories:**
   - every action of the five movement controllers, with its verb and the policy it will carry (P-3);
   - every gateway rule for HR.Core's mutating paths, and the rows the table will hold (P-4);
   - every user of `WorkflowStepAction`, `HrCore.BranchOperation` and `HrManagement.Approve`, the gateway and HR.Contracts included;
   - every caller of `WorkflowActionActor.FromPrincipal`, and every reader of the step history and of the step signatures;
   - the entity types a delegation can name, each with its consumer (`Delegation:EntityTypes`);
   - every test that posts an approval through the gateway, or builds an `ApprovalDelegation`;
   - the acceptance classes of P-11.
5. **The server, read-only:** the running tag, and P-13's picture.

Commit: `chore(iam050): baseline, the approval reach table, delegation readers, what PLAN-37..49 changed, the server's delegations, users and realm profile`.

## P1 — The override attribute, and the approver's reach in movements (plan §1, §2, P-3)

- **The attribute and the filter.**
- **The two policies,** and the attributes on the five controllers.
- **The gateway rule** for the movement approval paths. `Gateway.ApprovalReach` itself is built here, because this rule needs it (P-4).
- **The views:** the approver's default filter «بانتظار الاعتماد», and the buttons.
- **Not yet:** the delegate's part of `HrMovements.ApprovalAccess` arrives with the lookup, in P3 and P4.
- **Tests:**
  - the filter with and without the attribute, and the boot failure for an unknown policy;
  - `Iam050MovementApprovalPolicyTests`: per action and role, who passes and who gets 403;
  - the attribute never on a write action;
  - the gateway's exact match, with look-alike paths refused;
  - `Gateway.ApprovalReach` with Keycloak's own roles, a service account, and a missing or invalid `tenant_id`.

Commit: `feat(iam050): an action can name its own policy instead of the module's; the approver reads and approves the five movement types, and the creator role does not approve`.

## P2 — The gateway table, the approval-reach policy, employee corrections (plan §3, §4, P-4)

- **The table,** with today's rules moved in unchanged.
- **`EmployeeCorrectionsController`.**
- **The workflow approval paths** under `Gateway.ApprovalReach`.
- **Tests:**
  - `GatewayApproverReachTests` (add the `HR.Movements.Web` reference to the test project);
  - `GatewayPathAuthorizationMiddlewareTests`, updated;
  - the workflow approval paths: matched by segment, with look-alike paths refused.

Commit: `feat(iam050): one table of HR.Core's gateway exceptions; the approver reaches every approval action and the delegation page; the named approval paths admit any staff user and the service decides`.

## P3 — The delegation entity, its creation, the lookup (plan §5, P-5, P-8)

- **The entity, the matcher and `ValidateDelegation`.**
- **`HCM050_ApprovalDelegationScope`.**
- **`DelegationsController` and its views:** what the user created and what was delegated to him; everything for Admin and `platform-admin` only.
- **The lookups, the internal endpoint, the requirement and its handler, the acting context.**
- **The configuration:** `Delegation:DelegableRoles`, `Delegation:EntityTypes`, `Delegation:CacheSeconds`, `Authorization:NonStaffRoles`.
- **`employee_id`** in Auth.Directory, and the realm profile (P-5).
- **Tests:**
  - `Iam050DelegationMatcherTests`, and `ValidateDelegation` case by case;
  - `Iam050DelegationPostgreSqlTests` (the migration as the migrator under `FORCE`);
  - `Iam050DelegationInternalApiTests` (an interactive user 403, `svc-hr-movements` accepted, another client refused);
  - P-8's row-security proof;
  - the requirement with no lookup registered;
  - the provisioning payload.

Commit: `feat(iam050): one delegation entity with a mandatory role scope, a chosen employee and closed lists; old rows revoked; a lookup for services; employee_id for provisioned users`.

## P4 — The effect in the engine and in movements (plan §6, §7, P-6, P-7)

- **The engine:** the actor with his delegations, the evaluator, the history, the signature, the route policy on the five controllers, the inbox and the panels.
- **Movements:** the registration, `[ApprovalEntityType]`, separation of duties, `MOV050_ApprovalOnBehalfOf`, the display.
- **Tests:**
  - `Iam050EvaluatorDelegationTests`: a delegate; a principal who created; a delegate who created; an expired, revoked or wrong-type delegation; the direct role wins; a branch-scoped principal;
  - old history JSON still reads, and a direct signature is unchanged;
  - the movement approvals: on behalf, refused for the creator and for the principal's own request, fail-closed on a lookup error;
  - `Hcm025PanelServerParityTests`, green and unchanged.

Commit: `feat(iam050): a delegate approves the delegated role's step and the movement approvals inside the period and the scope, recorded on behalf of the principal, never past separation of duties`.

## P5 — Acceptance, then the independent review (plan Acceptance, P-9, P-10, P-11)

1. **The pieces of the acceptance run** (P-10): the class, `ci-scope.json`, `ac-ownership.json`, the personas' `scenarios`, `scripts/iam050-validate.ps1`.
2. **Acceptance.**
   1. Check C's free space (P-2). Start the stack (`acceptance-up.ps1 -Profile ProdLike`), and record the time to ready.
   2. Run `AcIam50ApproverReachDelegationTests` (AC-IAM-50a..e), positive and negative, under `RUN_ACCEPTANCE=true` and `ACCEPTANCE_REQUIRE_ALL=true`.
   3. Then run P-11's list, `scripts/iam050-validate.ps1`, and the seeded counts by psql. All required classes must Pass.
   4. `down -v`. Restore the committed evidence.
3. **Independent review** (one fresh subagent), before the server, so the single build carries its fixes. It reads this prompt, the plan and the diff, and hunts for:
   - a path the gateway now opens that is not a named approval action, or a look-alike path that matches;
   - an action in an opened controller with no explicit policy, or a write action under the override;
   - a user with only non-staff or Keycloak roles who passes `Gateway.ApprovalReach`;
   - a place where a delegation acts outside the decision points: a role in the token, a chain, a second delegation of a delegated role;
   - a delegate who gets more than his principal had: another role, another entity type, a wider scope, a longer period;
   - an identity rule that runs for the delegate only and not for the principal;
   - a lookup that answers for another tenant, admits an interactive user, or fails open;
   - a revoked or expired delegation that still acts, in HR.Core or after the cache time in movements;
   - a signature or a history entry that changed for direct actions;
   - something PLAN-49's tag cannot run beside.

   Use a refuter only for high findings. Fix what survives, with tests, and rerun only the classes the fixes touch.

Commit: `test(iam050): AC-IAM-50a..e pass on the prod-like stack; the lookup and the migration proved under forced row security; review fixes; CI scope and ownership updated`.

## P6 — `main` (merge `origin/main` only if it moved)

If `origin/main` moved, merge it (never rebase), keeping both sides in the shared list files. If another plan added migrations to the same context after yours, remove yours and add them again on top. Rebuild, and rerun the unit and integration tests, and the acceptance classes the merge touched. Record what the merged change means for you.

Commit (only if the merge needed fixes): `fix(iam050): main merged, conflicts resolved, migrations on top`.

## P7 — The demo server (P-13, P-14)

Record every server change and its undo command **as you make it**.

1. Check P-14's condition.
2. Take the backup, then **verify** it. Take the read-only before-picture.
3. **Build and deploy.** Run the build once, detached, on the final code. Deploy with `scripts/vps/deploy.ps1 -Tag <sha12>`:
   - start-up applies the two migrations;
   - if the deploy stops at `keycloak-reconcile` after PostgreSQL restarts, run the same command again (PLAN-40 incident).

   Record the downtime and the time to all healthy. Then add the demo user of P-13.
4. **Then check**, with evidence in `artifacts/plan50/vps/` (summarised in the final report, never in `acceptance-evidence/`). Run the server suites with nothing heavy beside them. The new checks go in an `AcIam50VpsChecks` class, and each one cleans up what it leaves open.
   1. **The migrations:** `HCM050_ApprovalDelegationScope` and `MOV050_ApprovalOnBehalfOf` are recorded; `WorkflowDelegations` is gone; the old rows are revoked as D-50-4 says (counts before and after).
   2. **The approver's reach:**
      - `e2e-checker-mgmt` opens the five movement lists through the gateway (200), and is refused on a create (403);
      - `e2e-movements-ops` is refused on an approve (403);
      - one transfer is created by `e2e-movements-ops`, approved by `e2e-checker-mgmt`, then cancelled by its creator.
   3. **A delegation, end to end:**
      - `e2e-checker-mgmt` delegates `hr-management-approver` to the new demo user for one day; both see it;
      - the delegate opens the transfers list through the gateway (200: the service lookup works on the server);
      - the delegate approves, on behalf, one transfer that `e2e-movements-ops` created, and its approval record names the principal;
      - if the server's users can also bring a workflow step that needs the role, the delegate approves one on behalf, and its history and signature name the principal. Otherwise the acceptance run is the proof for the engine, and the report says so;
      - after the revocation and the cache time, the transfers list answers 403.
   4. **The internal endpoint** refuses an interactive token (403).
   5. `scripts/vps/smoke.ps1 -RequireIntegrationWiring` is green.
   6. `AcOps36VpsBootTests` (36j excepted) and `AcInt37VpsServerTests` pass, with the PLAN-38..49 server checks. The AC-WF-46 sweeps check is rerun alone after a sweep cycle.
5. **If anything fails and cannot be fixed in the turn:** roll back or fix forward as P-14 says, prove the demo works, and record it.

Commit: `test(iam050): demo server on approver reach and delegation (old rows revoked, one approval on behalf), PLAN-36..49 server checks still green`.

## P8 — Docs, local gates, final report, merge through a PR (by you)

1. **Docs:**
   - `docs/SOD_MATRIX.md`: delegation, its roles and its limits, and the `sub` match of old rows;
   - `docs/WF_001_WORKFLOW_ENGINE.md` and `docs/WF_002_WORK_INBOX.md`: the real effect of a delegation;
   - `docs/HIRE_RBAC_MATRIX.md`: the approver's reach;
   - `docs/USER_GUIDE_AR.md`: the delegation page;
   - `OPS_VPS_SINGLE_HOST.md`: the new demo user, the backup location, the change table, the rollback note;
   - the «الحالة» lines for G-IAM-01, G-MOV-01 and G-WF-11 («جزئي — الأثر في المحرك والحركات»);
   - the plan's `Status` and «ملاحظات التنفيذ والانحرافات».
2. **Local gates, before any push:**
   - the full Testcontainers step on the host, with CI's filter;
   - `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci-local.ps1` on HEAD (with `-SkipTestcontainers` when the VPN is on);
   - then `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1`.

   All must be green. The report is committed before them, because a commit after a green local run needs another run.
3. **`reports/plan50-final-report.md`** (English), with these sections:
   1. Summary per phase.
   2. Commits.
   3. Decisions applied and deviations.
   4. **The reach table, before and after:** every approval action, its gateway rule, and who reaches it.
   5. **Delegation:** what a delegate can do and what he cannot, each line with the test that proves it.
   6. The guards and their own negative tests.
   7. Unit, integration and acceptance results; the PR run's artifact link as the evidence of record.
   8. **The server:** before and after, changes and their undo, the backup location, the rollback note.
   9. Deferred items.
   10. Defects for later plans.
   11. **Timing:**
       - total wall-clock;
       - active time, waiting for CI, and every stop with its cause;
       - the build, acceptance and deploy times;
       - Actions minutes;
       - the output of `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan-timing.ps1 -From 25 -To 50`.
   12. **A readiness note:**
       - for PLAN-51: the attribute is in place, the name list is still there, and the other movement controllers have no write policy yet;
       - for PLAN-52: where the branch scope of movements stands;
       - for PLAN-62: `employee_id` for provisioned users, and the state of the live realm's profile.
4. **Merge (P-15), by you:**
   1. Merge the feature branch into a new `release/plan-50` from `main` (`--no-ff`).
   2. Write `reports/plan50-pr.md` (short).
   3. `git push -u origin release/plan-50`
   4. `gh pr create --base main --head release/plan-50 --title "PLAN-50: the management approver reaches his approvals, and delegation has a real effect" --body-file reports/plan50-pr.md`
   5. Wait for the checks in the session («Waiting»).
   6. Only when every check is green: `gh pr merge <n> --merge`.
   7. A red check: fix it on the release branch, push that branch only, and wait again.

   Then `git switch main; git pull --ff-only`, and verify that `main` = `origin/main`. Record the PR link, each check result, the merge commit and the post-merge run in `reports/plan50-progress.md`, stamped `Merged by agent on owner instruction (UTC): <timestamp>`. Commit that note on a local branch `docs/plan-50-merge-note`, as the plans before did, so the next plan brings it in; **do not open a second PR for it.**

## Definition of done

- **The approver's reach:** `hr-management-approver` alone approves the five movement types and reaches the HR.Core approval actions through the gateway. He creates and executes nothing, and `hr-movements` does not approve.
- **`GatewayApproverReachTests` is green:** no approval action in HR.Core or HR.Movements lacks a gateway path the approver reaches, and the gateway opens nothing else.
- **Delegation works** in the engine and in movements, and «on behalf of» is in the history, the signature and the movement's approval record. It ends with a revocation or an expiry, and never passes separation of duties or the principal's scope.
- **One entity:** no `WorkflowDelegation`, and no delegation without a `RoleScope`.
- **Row security:** the lookups and the migration are proved as non-owner roles under `FORCE`.
- **The tests:** AC-IAM-50a..e = Pass (the PR run's artifact is the evidence of record), and the PLAN-25 and PLAN-36..49 suites are green.
- **The server** runs this code.
- **The documents** P8 lists are updated.
- **The merge** is done by you, through a PR, after green checks.

Then reply with a short summary in Arabic:
- what changed;
- what changed on the demo server (the revoked rows, the new demo user, the approval on behalf);
- how long the plan took, how long it waited for CI, and every stop with its cause;
- the PR link;
- the path of the final report.
