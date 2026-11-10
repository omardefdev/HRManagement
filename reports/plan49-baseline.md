# PLAN-49 — baseline (P0)

Plan: [PLAN-49-pol-engine-scope-orphans.md](../docs/acceptance-plans/PLAN-49-pol-engine-scope-orphans.md). Prompt:
[PLAN-49-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-49-AGENT-PROMPT.md) (it wins over the plan). Progress:
[plan49-progress.md](plan49-progress.md). Study: [POL_001_GAP_STUDY.md](../docs/POL_001_GAP_STUDY.md) (G-POL-02, 04, 09, 11).

Branch `feature/plan-49-pol-engine-scope-orphans` from `origin/main` f99950bf (PLAN-48, PR 46). The full inventories are in
`artifacts/plan49/` (not committed): `inventory-modes-clients.md`, `inventory-bindings-workflow.md`,
`inventory-tests-scripts.md`, `inventory-acceptance-ess.md`, `acceptance-before-*.txt`, `vps/before-p0-policy-picture.txt`.
Every line number below is of commit `ec9dca37` (the last commit before this plan's code).

## 0.1 Preconditions and first commits

All five hold (main folder, `origin/main` = f99950bf, a clean tree but the owner's `global.json` edit and the files this plan
never commits, `ssh hrm-vps whoami` = `hrm` with the host key matching the plan and tag `8abd5aa7fe93` running, `gh` signed in
as `omardefdev`). Side PRs 40 and 42 are open and were not touched.

| Commit | What |
|--------|------|
| `ec1b2729` | PLAN-48's merge note, cherry-picked from the local branch `docs/plan-48-merge-note` (deleted after the check) |
| `626a0022` | this plan's prompt |
| `ec9dca37` | the acceptance stack no longer binds ClamAV to a host port (PLAN-48 §10, P-2): port 53310 lies in a range Windows reserves on this machine (52576-53381), which is the "address already in use" of PLAN-48's CI flake as well |

## 0.2 Build and tests

| Step | Result | Time |
|------|--------|------|
| `dotnet build HRManagement.slnx -warnaserror` | 0 errors, 0 warnings | 109 s |
| Unit filter `PolicyEngine\|Pol047\|S2s042\|AssetClassifier\|WorkflowDefinitionsHaveHandlers\|Hcm025\|Hcm027\|Est024` | 620 / 620 passed | 51 s of tests (181 s with the start) |
| Integration filter `Pol047\|S2s042\|Hcm025` | 13 passed, 22 skipped, 0 failed | 15 s of tests (182 s) |
| `scripts/acceptance-up.ps1 -Profile ProdLike -NoBuild -SkipLiveVerification` (images of PLAN-48 still present) | up, exit 0 | 497 s |

The 22 skipped tests are the PostgreSQL ones: that run did not set `RUN_TESTCONTAINERS=true` (it was interrupted by a session
end and repeated with the same command). The filter `Pol047|S2s042|Ten048|Hcm025` with the variable ran next, by then on the P1
working tree: **47 / 47 passed** in 129 s of tests (178 s), so Testcontainers work on this machine today and no PostgreSQL test
of the policy engine was failing before this plan (`main` was green on PR 46 as well). The plan's two other integration names,
`Fun002` and `Est002`, are in the P1 and P3 runs.

The four inventories ran as one read-only workflow, two agents at a time: 61 minutes, in parallel with the stack start and P1's
first code.

## 0.3 The domain reality table (the study's §2.1, re-checked on `ec9dca37` and on the demo server)

"Rules" is the effective national version on the demo server (tag `8abd5aa7fe93`); "Mode" is Production = Staging file = the
single-host chain. No `PolicyEngine` key exists in any k8s or VPS file; only HR.Core has a section, with `Core.Recruitment`.

| Domain | Owner | Call sites today | Mode | Rules (server) | Ledger rows (server) | Verdict |
|--------|-------|------------------|------|----------------|----------------------|---------|
| Assets.Custody | Admin.Logistics | `AssetClassifier.cs` (Classify), `AssetsController.cs` (Create, Assign), through `AssetPolicyFactsBuilder.cs:21, 42, 52` | Off (the client's static shim decides) | 6; `R-PRIV-WF` and `R-HIGH-APPROVAL` have no `action` condition, `R-PRIV-EVIDENCE` neither | 0 | **Governed** |
| IDCard.Lifecycle | IDCard | `CardRequestsController.cs` (Process) | Off | 1: `R-ID-PROCESS` → `TriggerWorkflow ID-CARD-ISSUANCE` (orphan: the card starts its approval by PLAN-46's contract, never by the engine) | 0 | **Governed** |
| Contracts.Leave | HR.Contracts | `LeaveRequestHandlers.cs` (Create by HR only; `TenantId = null`) | Off | 3: `R-LEAVE-SELF` (the fact is never sent), `R-LEAVE-MAX` (Deny above 30, a placeholder), `R-LEAVE-LONG` (`RequireApproval`, no consumer) | 0 | **Governed** |
| Core.Recruitment | HR.Core (in process) | requisition (SubmitForApproval, Publish, PublishInternal), offer (Issue), committee (SubmitCommittee), appeal (FileAppeal), `RecruitmentApplicationsController` (Hire) | **Shadow** | 10 (`R-REC-HIRE` retired by PLAN-47's set) | 15, all Shadow, caller HR.Core | **Governed** |
| Core.Establishment | HR.Core (in process) | `EstablishmentChangeLifecycleService.cs:99, 144` (SubmitForApproval) | Off | 2: `R-EST-SUBMIT` (its code has **no binding**), `R-EST-DENY-BRANCH` (never matches: `hasOrgScope: false` is a constant) | 0 | **Governed** |
| Assets.Procurement | Admin.Logistics | none | Off | 2 | 0 | out of scope |
| Contracts.Advance | HR.Contracts | none (`ForAdvanceRequest` has no caller, production or test) | Off | 1 | 0 | out of scope |
| Contracts.Hire | HR.Contracts | none | Off | 1 → retired code `HIRE-WIZARD` | 0 | out of scope |
| Contracts.Separation | HR.Contracts | none | Off | 1 → retired code `SEPARATION-WIZARD` | 0 | out of scope |
| Contracts.Contract | HR.Contracts | none | Off | 1 | 0 | out of scope |
| Movements.Transfer | HR.Movements | none | Off | 1 → retired code `TRANSFER-APPROVAL` | 0 | out of scope |
| Movements.Workforce | HR.Movements | none | Off | 0 | 0 | out of scope |
| Core.Employment | HR.Core | none | Off | 0 | 0 | out of scope |
| Health.Insurance | HR.Health | none | Off | 1 | 0 | out of scope |
| Compliance.Disciplinary | Compliance | none | Off | 1 → `DISCIPLINARY-APPROVAL` (starts nothing: PLAN-46 starts it by contract) | 0 | out of scope |
| Payroll.Period | Payroll | none | Off | 1 | 0 | out of scope |
| IT.Tickets | IT.Infra | none | Off | 1 → retired code `IT-TICKET` | 0 | out of scope |
| Archive.Retention | Records.Archive | none | Off | 2 | 0 | out of scope |
| Training.Learning | HR.Training | none | Off | 1 | 0 | out of scope |
| Analytics.ETL | Dev.Analytics | none | Off | 1, on version 5 (PLAN-47's and PLAN-48's server tours copied and rolled it back) | 0 | out of scope |

**What changed since the study (`f9eab1a`):** the service path is the internal endpoint with an owner table of 17 domains and
11 clients (PLAN-42); a validator, change sets, four eyes and immutability (PLAN-47); the agency ceiling and the national read
(PLAN-48). `R-REC-HIRE` is gone from new databases and retired where an old one had it. **What did not change:** the five
domains with a call site are the same five; fifteen have none; every count of the study's table holds on the server, where all
twenty templates are still on version 1 with the rules that older releases wrote in place (the PLAN-47 baseline was a NoOp for
the four templates it names).

## 0.4 Names this plan uses (from PLAN-42, PLAN-46, PLAN-47, PLAN-48)

- **PLAN-42:** `POST api/internal/policies/enforce` (`PolicyEnforceInternalController`, policy `ServicePolicies.PolicyEnforceS2S`,
  scope `hr-core.workflow`: there is no scope of its own); `PolicyDomainOwners`; `PolicyEnforceClient` with the local mode, the
  server-derived mode and `ShadowDecision`; `PolicyEngineConfigurationValidator` (an `IHrStartupCheck`); the ledger columns
  `CallerClientId` and `Mode`.
- **PLAN-43 / PLAN-46:** `ExternalApprovalService` and `ExternalApprovalTypes` (a registered type starts only there and reads no
  binding); `PolicyWorkflowTriggerAllowlist` (empty); `WorkflowPackSeeder.RetiredCodes`; the outcome consumers of
  `WorkflowDefinitionsHaveHandlersTests`.
- **PLAN-47:** `PolicyRuleValidator` + `PolicyRuleValidationContext` + `IPolicyDomainCapabilities` (allow-all at HEAD);
  `PolicyDraftService`, `PolicyPublishService.PublishAsync` / `PublishSystemSeedAsync`; `PolicySeedChangeSets` and
  `PolicySeedChangeSetApplier` (append-only sets, a digest per domain, one transaction per template under an advisory lock, an
  open draft postpones the set); `WithdrawsAllRules`; the start-up log "PLAN-47 validator inventory".
- **PLAN-48:** `PolicyCeiling`; the national read (`OwnOrNational`, `NationalOnly`); national seeding in the platform session
  (`NationalSeedOrchestrator`: workflow pack, policy engine, code generation, in that order).

## 0.5 Modes and the remote client, everywhere

Counts (full tables in `inventory-modes-clients.md`):

| What | Count |
|------|-------|
| Hosts | 15 (14 services and the gateway) |
| `appsettings*.json` with a `PolicyEngine` section | 2: HR.Core Production and Staging, `Core.Recruitment = Shadow` |
| Compose lines that set a mode | 6 in 4 files: lab `Assets.Custody = Enforce` on hr-core-web; prod-like `Core.Recruitment = Shadow`; the s2s42 and wf46 overlays |
| Mode keys in `deploy/` (k8s, VPS, wiring), workflows, `.env*` | 0 |
| A mode key for an out-of-scope domain, anywhere | 0 |
| Services that call `AddHrPolicyEnforceClient` | 11 |
| … that inject `IPolicyEnforceClient` | 3: Admin.Logistics, IDCard, HR.Contracts |
| … that never do (the removal list) | 8: Dev.Analytics, HR.Health, HR.Training, IT.Infra, Payroll, HR.Movements, Records.Archive, Compliance |
| … of those, left with no reader of `Integration:HrCoreBaseUrl` | 3: Dev.Analytics, HR.Health, HR.Training |

Facts that shape the plan:

1. **`Staging` is selected by no deployment.** `ASPNETCORE_ENVIRONMENT` is `Development`, `Acceptance` or `Production`; the k8s
   `staging-ha` overlay runs as Production. A Staging file exists for the Gateway, HR.Contracts, HR.Core and Payroll only, so
   P-9 creates the Admin.Logistics and IDCard ones.
2. **The prod-like chain carries an Enforce today.** `docker-compose.acceptance.yml:103` (lab, needed by AC-10 and AC-11) is
   part of the prod-like chain, so prod-like hr-core-web has `Assets.Custody = Enforce` while Admin.Logistics has no key. The
   comment at `docker-compose.acceptance-prodlike.yml:21` ("no other key") is wrong about the merged chain. The Shadow keys of P4
   must say `Assets.Custody: "Shadow"` there explicitly, and the consistency test must read the merged chain.
3. **The validator rejected PLAN-47's own settings.** A `PolicyEngine:Roles`, `:Seeds` or `:Publish` key stopped the boot (the
   validator took the section name for a domain). No deployment sets them, so nothing broke; fixed in P1 with a test.
4. **The start-up check ran in 12 of 15 hosts** (not the Gateway, Auth.Directory, File.Storage).
5. **`PolicyEnforceS2S` admitted three clients with no enforce call** (`svc-records-archive`, `svc-it-infra`, `svc-compliance`:
   owners in the table, holders of `hr-core.workflow` for other reasons). They got a neutral 200 on their own domain.
6. **Dotted variable names survive only under bash** (`docker/DotnetService.Dockerfile:57-58`); a container's mode is read from
   `/proc/1/environ`, not from `sh -c env`.
7. **AC-WF-46j probably never ran with the server in Enforce** (read, not executed): in the stage `prodLike:s2s42`,
   `AcS2s42TrustBoundaryTests` recreates hr-core-web from a file list without the wf46 overlay before `AcWf46PolicyOrphanTests`
   runs. The pol49 overlay class therefore asserts the container's mode itself.

## 0.6 Bindings and their readers (P-5)

One writer: `PolicyEngineSeeder.SeedWorkflowBindingsAsync` (additive; it never updates or removes a row). Three readers:
`WorkflowStarter` (a binding pins a definition version, and only when the caller passes a policy domain), the publish warning,
and the validator (`THEN_WORKFLOW_UNBOUND`). `ExternalApprovalService` reads none.

The server's 13 rows: Assets.Custody ×2 and IDCard, Compliance, Contracts.Hire, Contracts.Separation, IT.Tickets,
Movements.Transfer ×1 each, **all eight pinned to a retired version 1**; Core.Recruitment ×5 (three pinned to a published v1,
two unpinned). None for Core.Establishment.

| Fact | Consequence |
|------|-------------|
| `R-EST-SUBMIT` triggers `ESTABLISHMENT-CHANGE-APPROVAL`, which is published and handled but **unbound** | P2 adds the binding, **unpinned** (a pin would break AC-WF-43c, which publishes a new version of that code) |
| Only the five Core.Recruitment rows are needed for a new database to boot; `ASSET-ASSIGN-APPROVAL` becomes one as soon as a set touches `R-PRIV-WF` / `R-HIGH-APPROVAL` | a new database keeps the governed codes |
| The bindings of ID-CARD-ISSUANCE, ASSET-LOSS-APPROVAL, DISCIPLINARY-APPROVAL and the four retired codes are read by nothing that changes behaviour (PLAN-46 uses the **definitions**, not the bindings) | a new database does not seed them; an existing one keeps its rows (nothing removes a binding) |
| `NationalSeedTests.cs:74` asserts `bindings.Count >= 9` | the test changes with the rule (seven governed rows) |
| The entity-type criterion of `WorkflowDefinitionsHaveHandlersTests` does not catch `R-ID-PROCESS` or `R-DISC` (their types have a consumer) | "no orphan trigger" is proved by the catalog's triggers per entity type, not by the entity type alone |
| Seeded v1 rules cannot be listed without a `DbContext` | `Pol049FreshSeedTests` seeds a context, then validates |

Workflow codes that seeded rules trigger on a new database at HEAD: 15 rules, 13 codes: 4 fire a retired code, 1 an unbound
code, 10 a bound published code.

## 0.7 Open drafts and agency overrides in the domains the sets touch (P-4)

- **Demo server:** 0 open drafts; 20 national templates and **no agency override** at all; `Analytics.ETL` has versions 1-5.
  Five seed applications, all NoOp. So every `POL-049-*` set applies at the first boot, and the retirement publishes one final
  version per out-of-scope template (15).
- **Acceptance database (a new prod-like stack on `ec9dca37`):** 23 templates: the 20 national ones and three Assets.Custody
  overrides (agencies A, B, C; version 2 published, and each carries a version 3 draft with `ACC-R-PRIV-DENY`). **No override
  in an out-of-scope domain**, and no national draft. The baseline published version 2 of Contracts.Leave, Core.Recruitment and
  Archive.Retention there. The overrides of B and C hold rules the ceiling drops on purpose (AC-TEN-48e); they are written by
  `PolicySeedVersionWriter`, never validated, and the start-up inventory (national session) does not list overrides. They stay
  as they are. The ledger is empty on a new stack.
- A change set skips a template with an open draft and applies at a later boot (PLAN-47); the retirement keeps that rule.

## 0.8 The start-up inventory today

A new acceptance database logs six refused rules: `R-HIRE-START`, `R-SEP-START`, `R-TKT`, `R-TRF` (`THEN_WORKFLOW_RETIRED`),
`R-EST-SUBMIT` (`THEN_WORKFLOW_UNBOUND`) and `R-PRIV-CODE` (`THEN_CODE_POLICY_UNKNOWN`). The last one is an ordering artefact:
the inventory runs inside `PolicyEngineSeeder`, before `CodeGenerationSeeder` has written `ASSET-PRIVILEGED`; a second boot
does not list it. The inventory also reads **every** Published version, not only the effective one, so with a real catalog it
would list each historical rule of the retired templates on every boot. P2 moves it after the code-generation seed and splits
"effective" from "historic".

## 0.9 Facts against their schemas (G-POL-04) and the leave channels

| Builder | Sends | Schema | Result today |
|---------|-------|--------|--------------|
| `ForIDCardRequest` | `employeeId`, `purpose`, `action` | `purpose`, `action` | the strict evaluation refuses `employeeId`: in Enforce the card request would be refused whole |
| `ForLeaveRequest` | `employeeId`, `durationDays`, `leaveType`, `action` | `durationDays`, `leaveType`, `action`, `isSelfApproval` | same; `isSelfApproval` is never sent, so `R-LEAVE-SELF` cannot match |
| `ForAdvanceRequest` | — | — | no caller at all; removed |
| establishment | `hasOrgScope: false` (a constant) | `hasOrgScope` | `R-EST-DENY-BRANCH` never matches |
| `AssetPolicyFactsBuilder` | fields of the schema | match | nothing to fix |

Leave reaches the engine on one of four channels: `CreateLeaveRequestHandler` (HR creates; it sends `TenantId = null`). Update
and Approve do not ask; Approve runs inside `repository.ExecuteAtomicAsync`.

**The self-service path** (`EssPortalController.Leave` in HR.Core publishes `ILeaveRequested`; `LeaveRequestedConsumer` and
`ProcessLeaveRequestedHandler` in HR.Contracts consume it):

- The portal checks only that the end is not before the start. It sends **calendar** days (`End - Start + 1`), a free-text leave
  type (its list offers `Emergency`, which HR.Contracts does not have and maps to `Other`), no tenant in the body (the
  `X-Tenant-ID` header carries it and `TenantIdConsumeFilter` sets `TenantIdFlow`) and no user.
- The handler validates nothing and asks no policy: it creates the service transaction and a `Pending` request with the event's
  `DurationDays`. `LeaveAndAdvanceRules.ValidateLeaveRequest` (the HR path) counts **working** days (Friday and Saturday do not
  count) and refuses a duration that differs from them, so handing it the event's number would refuse every period that holds a
  weekend. P3 computes the working days in HR.Contracts for a self-service request, as `CreateLeaveRequestHandler` does, and
  stores that number: both channels then send the engine the same fact. (A behaviour change: the stored duration of a
  self-service leave over a weekend becomes its working days.)
- A consumer exception is retried three times and then parked in the error queue, so a local refusal must not throw: the request
  is created `Rejected` with the reason (D-POL-14 for the engine's refusal in Enforce, the same shape for the local one).
- Redelivery is covered three times over (the consumer outbox, PLAN-40's receipt, the handler's check by `RequestId`).
- **No test drives this path today**: no acceptance, E2E, integration or unit test opens the ESS leave page or publishes
  `ILeaveRequested` (the prompt's note that AC-INT-37 does is out of date: 37a creates its leave on the HR page), and the persona
  `employee-a` is used by no test. One unit test calls the handler, with a Tuesday-to-Saturday period and "5 days".

**The approval's `isSelfApproval`.** The page passes the approver's Keycloak user id (`sub`), never an employee id; the request
knows its employee (`EmployeeId`) and its creator (`CreatedBy`, a user id; null for a self-service request). The local
segregation check already refuses `ApproverId == CreatedBy` before anything else, so with the plan's formula
(`ApproverId == CreatedBy`) the fact could never be true when the engine is asked, and `R-LEAVE-SELF` would stay a dead rule.
P3 therefore also compares the approver's `employee_id` claim with the request's employee: the case the rule's own reason names
(«لا يمكن الموافقة على إجازتك»), and the one the local check does not see (a leave that someone else, or the portal, created for
the approver).

**The establishment guard.** Both controllers refuse a branch-scoped actor with 403 before the service is called
(`establishmentScope.ValidateCentralWriteAsync(User)`), and "unrestricted" there is exactly `!IsBranchScoped(User)`. So
`actorIsBranchScoped` is always false at both call sites as they stand, and `R-EST-DENY-BRANCH` repeats a guard the page already
has. The fact becomes real in P3 as the plan says; but **AC-POL-49e cannot be written as the plan has it** («`br1-mgr` يقدّم …
والتقديم يمضي بمساره»): the branch manager never reaches the engine, and removing the guard to let him would weaken it. The
scenario is adapted in P5 (the central actor's submission is recorded with no match of the deny rule; the branch manager gets
403 and leaves no ledger row; the rule's match on the true fact is proved by a simulation and by unit tests of the service).

## 0.10 Tests, scripts and documents the change reaches

- **Unit:** the owner assertions of `S2s042PolicyEnforceInternalTests` (every non-`Core.` domain owned; 403 for
  `Core.Employment`), five PLAN-47 tests that seed, roll back or apply a set in `Movements.Transfer` / `Archive.Retention`, and
  the PLAN-48 tests that pass only because the capabilities allow everything (`RequireDualApproval`, `RequireOriginReceipt` in
  custody; `RequireEvidence` in leave).
- **The only agency overlay pack fails a real catalog:** `seed-packs/policy-overlays/policy-ministry-finance-v1.json` puts
  `RequireEvidence` on Contracts.Leave, which consumes Allow and Deny only.
- **Acceptance (required in CI):** `AcTen48NationalScopeTests` 48e step 1 drafts and publishes in `Analytics.ETL`;
  `AcMsg40AtomicPublishingTests` 40c publishes three overrides of `Training.Learning`. The server tour `AcPol47VpsChecks`
  (not in CI) copies and rolls back `Analytics.ETL`. All three move to a governed domain.
- **Scripts that pin source text:** `wf043-validate.ps1` (the empty allow-list literal), `hcm025-validate.ps1` (the
  `Core.Recruitment = Shadow` key in two files and one compose line, the text of the `R-REC-HIRE` retirement, and
  `PolicyStaticFallback.cs` by path), `est002-validate.ps1` (`R-EST-SUBMIT` in `PolicyEngineDomainSeeds.cs`),
  `seed-001-validate.ps1` (`ID-CARD-ISSUANCE` in `PolicyEngineSeeder.cs`). None of the four runs in CI.
- **Fresh-database assertions a set can disturb:** `Pol047SeedChangeSetApplierTests` (custody keeps one version, recruitment
  has versions 1 and 2, two applications), `Pol047SeedApplierPostgresTests` (at least three published applications, "Leave,
  Recruitment and Archive"), `NationalSeedTests` (nine bindings).
- **Documents:** `docs/POLICY_DOMAIN_REGISTRY.md` lists 13 rows, three of them wildcards, and calls IDCard "Static";
  `docs/policy-rules-inventory.csv` is stale (31 rows against 64 `*Rules.cs` files) and is overwritten by
  `scripts/policy-inventory.ps1`, so a hand-added column would be lost.
- **The profile detector** (`AcceptanceRunContext.DetectProfile`) reads `PolicyEngine__Core.Recruitment=Shadow;` on hr-core-web:
  a stack recreated with that domain in Enforce is recorded as "lab" unless `ACCEPTANCE_PROFILE` is set (the job sets it).
- **Handlers built by hand in tests** (a new constructor argument reaches them): `ApproveLeaveRequestHandler` in
  `Ops038DemoDefectTests` (five places) and through MediatR in `OutboxAtomicityIntegrationTests` and `SoDPolicyIntegrationTests`
  (their containers register no policy client), `ProcessLeaveRequestedHandler` in `InboundServiceTransactionTests`;
  `SubmitForApprovalAsync` in six test files. `FakePolicyEnforceClient` (TestSupport) records every request and answers what a
  test tells it, Shadow included.

### The acceptance classes of P-11, and what each drives

| Class | Stage | What it drives of this plan |
|-------|-------|-----------------------------|
| `Ac09ShadowPolicyTests`, `Ac10EnforcePolicyTests`, `Ac11PolicyWorkflowTests` | lab main | custody: simulate, publish agency A's draft, the internal endpoint in Enforce (`AssetAssignment` + `Assign`) |
| `Ac32LogisticsScopeLiveTests`, `Ac33AssetLossLiveTests` | lab main | custody pages; the loss report asks no policy |
| `AcEst24EstablishmentWorkflowUiTests`, `AcEst15…`, `AcEst17…`, `AcHcm26…` (isolated) | lab | the establishment submit (central actors; `br1-mgr` is refused before the service) |
| `AcHcm21RequisitionApprovalUiTests`, `AcWf23WorkInboxUiTests`, `AcHcm27…` (isolated), the lab journey | lab | recruitment submits through the coordinator |
| `AcHcm25ProductionApprovalTests` | prod-like main | recruitment under Shadow; reads `PolicyRules` and the mode key of hr-core-web |
| `AcWf43DefinitionGovernanceTests`, `AcWf45AssetApprovalLoopTests` | prod-like | a privileged handover through the page |
| `AcTen48NationalScopeTests` | prod-like main | evaluations of custody; **48e step 1 works in `Analytics.ETL`** |
| `AcPol47RuleLifecycleTests` | prod-like isolated | agency A's custody override; two restarts must add no version and no seed application |
| `AcMsg40AtomicPublishingTests` | prod-like isolated | **40c publishes in `Training.Learning`**; 40d creates and approves a leave on the page |
| `AcInt37ProductionWiringTests` | prod-like isolated | a leave created on the HR page, an asset, a card |
| `AcWf46CrossServiceApprovalTests` | prod-like isolated | the card's `ProcessRequest` |
| `AcS2s42TrustBoundaryTests`, `AcWf46PolicyOrphanTests` | prod-like s2s42 | custody in Enforce and Shadow on both sides; the card in Enforce (its text names `R-ID-PROCESS`) |
| `AcTen48LeastPrivilegeTests` | least privilege | a snapshot and an evaluation as a non-owner role |
| `AcOps36VpsBootTests` and its partials | server only | leave on the page; the `Analytics.ETL` tour of PLAN-47 |

Under Shadow, five call sites make a live call to HR.Core for the first time outside the overlays: the asset classify, create
and assign, the card's process, and the leave create.

## 0.11 The demo server before this plan (read-only)

Tag `8abd5aa7fe93` (PLAN-48; the log-line fix `95228ffa` was never deployed). Last HR.Core migration
`20261108125046_POL047_PolicyRuleLifecycle`. 20 national templates, 22 effective rules in the five governed domains, 0 drafts,
0 overrides, 13 bindings, 21 workflow definitions, 5 seed applications (all NoOp), ledger: `Core.Recruitment` Shadow ×15 from
HR.Core between 2026-11-06 and 2026-11-09, nothing else. Subject type of the custody template: `Asset`.

## 0.12 Decisions the inventories settled (taken in P0, recorded in the progress file)

1. **Out of scope is answered before ownership** at the internal endpoint: a domain the catalog retires answers 400 with
   «المجال خارج نطاق المحرك» for any caller, then 403 for a client that does not own a governed domain.
2. **The start-up check runs in every host** (registered by `AddHrProductionNetworking`), so a mode key for a retired domain
   stops any of the 15.
3. **A new database keeps the governed bindings only** (seven rows with the new establishment one); existing rows stay.
4. **The finance overlay pack gets a rule the catalog accepts** (custody, `Assign`, `RequireEvidence`) in place of the leave
   rule nobody consumed.
5. **The retired templates are not deleted** and their history is not rewritten: one final version with no rule, published as
   a system seed, and the template marked retired (P-3).
