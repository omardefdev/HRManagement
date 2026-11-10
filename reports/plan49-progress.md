Started (UTC): 2026-11-10T08:35:40.2725416Z

# PLAN-49 — progress checkpoint

Specification: `docs/acceptance-plans/execution/PLAN-49-AGENT-PROMPT.md` (it wins over the plan
`docs/acceptance-plans/PLAN-49-pol-engine-scope-orphans.md`). Branch `feature/plan-49-pol-engine-scope-orphans`, cut from
`origin/main` f99950bf. One session, in the main folder, no second clone.

## Stamps

- Started (UTC): 2026-11-10T08:35:40Z (first line of this file).
- Resumed (UTC): 2026-11-10T09:00:38Z — the desktop session ended during P0 (while the baseline integration filter ran; Docker
  Desktop had restarted) and the owner said «اكمل». Nothing was lost: the two first commits were in place, the baseline build and
  the unit filter had finished, and only the integration filter had to be run again. No time waited for the owner beyond the
  restart itself (about 17 minutes between the last tool call and the resume).
- Resumed (UTC): 2026-11-10T09:58:14Z — the context was compacted in the middle of P0 (the inventories were being read; P1's
  code was already in the working tree, uncommitted). Read this file and `git log --oneline -20` first, as the prompt orders. No
  owner wait: the session went on by itself.
- Resumed (UTC): 2026-11-10T11:53:50Z — the context was compacted near the end of P4 (its code and tests were in the working
  tree, uncommitted; one PostgreSQL assertion on the audit row's tenant was failing). Read this file and `git log --oneline -20`
  first. `origin/main` and the server were not touched in P4, so nothing remote had to be checked. No owner wait.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-10T10:10Z |
| P1 | done | 2026-11-10T10:20Z |
| P2 | done | 2026-11-10T10:50Z |
| P3 | done | 2026-11-10T11:20Z |
| P4 | done | 2026-11-10T12:25Z |
| P5 | pending | |
| P6 | pending | |
| P7 | pending | |
| P8 | pending | |

## Durations

| What | Time |
|------|------|
| Baseline build `-warnaserror` | 109 s |
| Baseline unit filter (620 tests) | 51 s of tests, 181 s with the start |
| Baseline integration filter, without Testcontainers (13 passed, 22 skipped) | 15 s of tests, 182 s |
| Prod-like stack start on `ec9dca37`, images of PLAN-48 (`acceptance-up.ps1 -Profile ProdLike -NoBuild -SkipLiveVerification`) | 497 s |
| The four inventories (one read-only workflow, two agents at a time) | 61 min, beside the stack start and P1's first code |
| Full unit suite on the P1 tree (5915 tests), twice | 114 s and 119 s of tests (378 s with the start, beside another project's containers) |
| PostgreSQL filter `Pol047\|S2s042\|Ten048\|Hcm025` with `RUN_TESTCONTAINERS=true` on the P1 tree (47 tests) | 129 s of tests, 178 s |
| Integration filter `Fun002\|Est00\|RecruitmentRequisitionPolicy\|InternalChannel\|Hcm033\|IntegrationHostValidation` (32 tests) | 52 s of tests, 80 s |
| Build `-warnaserror` at the end of P1 | 22 s (incremental) |
| Build `-warnaserror` at the end of P2 (whole solution) | 61 s |
| Full unit suite on the P2 tree (5943 tests) | 85 s of tests, 101 s |
| PostgreSQL filter `Pol049\|Pol047\|Ten048\|S2s042\|Hcm025` on the P2 tree (50 tests) | 195 s of tests, 256 s |
| Full unit suite on the P3 tree (5969 tests) | 188 s of tests, 217 s |
| Integration filter of P3 (86 tests: leave, establishment, outbox, hosts, `Hcm025`, `Fun002`) | 248 s of tests, 304 s |
| Build `-warnaserror` at the end of P4 (whole solution, incremental) | 29 s |
| Full unit suite on the P4 tree (5993 tests) | 166 s of tests, 188 s |
| PostgreSQL filter of P4 (`Pol049\|Pol047\|Ten048\|S2s042\|Hcm025\|Hcm031\|Est00\|RecruitmentRequisitionPolicy\|InternalChannel`, 73 tests) | 364 s of tests |
| The seven validation scripts at the end of P4 | 88 s together (`pol049-validate` with its tests: 51 s) |
| Waiting for CI | 0 so far |
| Waiting for the owner | about 17 min (the session end of 08:43Z to 09:00Z; nothing was asked of the owner) |

## P0 — done

**Preconditions (all hold, checked 08:36Z and again at each resume):**
1. Main folder, on `docs/plan-48-merge-note` at the start; no stack and no container of this repository was running.
2. `origin/main` = f99950bf (PLAN-48, PR 46); still f99950bf at 10:06Z.
3. Working tree: the owner's `global.json` edit, `tmp-vendors.html`, the untracked prompt file; test-rewritten
   `testdata/*/out/*.json` and the two nested reports restored by path (never a blanket checkout of `testdata`).
4. `ssh hrm-vps whoami` = `hrm`; the host key matches the plan; running tag `8abd5aa7fe93`.
5. `gh` signed in as `omardefdev` with `repo` and `workflow`. Side PRs 40 and 42 are still open: not touched.

**Commits:** `ec1b2729` (PLAN-48's merge note, cherry-picked from `ea91a632`, identical tree), `626a0022` (this plan's prompt),
`ec9dca37` (ClamAV keeps no host port), then the P0 commit (the baseline report and this file). `docs/plan-48-merge-note`
deleted after the check (its only commit is on this branch). The feature branch tracks nothing (`--unset-upstream`), so a bare
`git push` cannot reach `main`.

**The ClamAV port (P-2):** the mapping is dropped (`ports: !reset []`, and the fixed container name with it). Nothing on the
host read 53310. On this machine the port lies in a range Windows reserves (52576-53381 today), which is why PLAN-48 could not
start the stack here. With the fix the prod-like stack starts (497 s). Reserved ranges today: 49722-49921, 50000-50259,
52576-53381, 58480-58579, 64957-65056; no other host port of the stack is in one.

**Baseline and inventories:** `reports/plan49-baseline.md`. Raw material in `artifacts/plan49/` (four inventory files, the
acceptance and server before-pictures).

**Docker:** another project's containers (`cgims-*`, `p6check-*`, `p6-sqlcheck`) run on this machine and are not this
repository's: never touched. Their host ports are compared with the stack's before each stack start (P-2).

## P1 — done

One catalog, `PolicyDomainCatalog` (SharedKernel): a descriptor for each of the twenty domains. Five are `Governed` (owner
service and client, entity types and their actions, the effects a call site consumes, the `SetField` fields, the workflow codes
a rule may name per entity type and who starts them, the call-site files); fifteen are `OutOfScope`, each with its reason in
Arabic. Everything else reads it:

- **The validator.** `IPolicyDomainCapabilities` grew (`IsGoverned`, `Actions`, `SubjectTypesOf`, `AllowsWorkflowCode` by entity
  type) and `CatalogPolicyDomainCapabilities` is its only implementation; `AllowAllPolicyDomainCapabilities` is gone from every
  path, the applier's hand-built services and the test helpers included. New refusals, all Arabic: `DOMAIN_OUT_OF_SCOPE`,
  `WHEN_ACTION_UNKNOWN`, `WHEN_ACTION_OPERATOR`, `THEN_WORKFLOW_NOT_ALLOWED` (a rule that can match an action whose entity type
  does not own the code: `PolicyRuleActionScope` works out which actions a condition can match). The draft service refuses a
  draft, a copy, a rollback and a withdrawal in an out-of-scope domain; the publish service refuses any publication there but
  the retirement (P2).
- **Run time.** `PolicyOrchestrator` answers a neutral decision («المجال خارج نطاق المحرك») with no ledger row and no workflow;
  `PolicyModeResolver` answers Off whatever `DefaultMode` says; the internal endpoint answers 400 (then 403 for a client that
  does not own a governed domain, then 400 for a subject type or an **action** the catalog does not list for it).
- **Start-up.** `PolicyEngineConfigurationValidator` stops any of the 15 hosts where an out-of-scope domain's key is not Off
  (it names the key, never the value), and no longer takes `Roles`, `Seeds`, `Publish` for domains.
- **Derived, not duplicated.** `PolicyDomainOwners` and `PolicyWorkflowTriggerAllowlist` are views of the catalog (the
  allow-list is empty: the catalog marks no trigger for the internal endpoint). `PolicyDomainRegistry` is deleted; the page's
  table (status, owner, mode from the resolver, last ledger row, call sites or reason) comes from the catalog, and
  `CreateDraft` refuses an out-of-scope domain on the page and in the API. `docs/POLICY_DOMAIN_REGISTRY.md` is written from the
  catalog by a test and compared with it by the same test.
- **The client** is registered only by Admin.Logistics, IDCard and HR.Contracts. Removed from the eight others; the address key
  `Integration__HrCoreBaseUrl` left Dev.Analytics, HR.Health and HR.Training (nothing else read it there) in the wiring file,
  the compose file and the three k8s patches; the five others keep it for a reader they still have. No Keycloak client, scope
  or secret was touched. `PolicyDomainGateway` (no caller, production or test) is deleted.
- **`scripts/pol049-validate.ps1`** (ASCII, PowerShell 5.1): catalog = decision D-POL-1 = registry document = page; the client
  only where a governed domain is owned and injected; no mode key for an out-of-scope domain in any shipped configuration.
  Checked against three planted defects (a mode key, a missing registry row, a registration without a call): each fails.

**Tests of P1:** `Pol049PolicyDomainCatalogTests` (10), `Pol049CatalogCapabilitiesTests`, `Pol049OutOfScopeTests` (134 `Pol049`
cases in all); changed to the new rule, never weakened: `S2s042PolicyEnforceInternalTests` (out-of-scope 400, the action check,
owners from the catalog), `Pol047*` (the tests that worked in Movements.Transfer and Archive.Retention use Core.Recruitment),
`Ten048*` (effects the catalog gives the domain), `ProductionWiringMatrixTests` (`PolicyEnforceS2S` joins the list of policies
with exactly their matrix callers: the three owners), `Ac10EnforcePolicyTests` (the pair the call site sends,
`AssetAssignment` + `Assign`), `Pol047SeedApplierPostgresTests` (the baseline publishes nothing in Archive.Retention).

**Validators:** `pol049-validate` PASS; `wf043-validate` PASS (its allow-list check now reads "derived from the catalog, and
the catalog marks no trigger for the internal endpoint"); `pol047-validate` PASS; `hcm025-validate -SkipTests` PASS;
`int037-wiring-check` PASS (compose, prod-like parity, three k8s overlays); `lint-compose` PASS (both chains);
`int037-render-wiring-doc -Check` PASS.

**Formatting:** the Windows `dotnet format` run on the changed files reports only end-of-line trivia at comments inside
parameter lists (it expects CRLF on Windows; the repository is LF by `.gitattributes`). One of the three places is PLAN-47's
own code. The authoritative gate is ci-local's Linux container, run in P8.

## P2 — done

- **The migration** `20261110102522_POL049_PolicyDomainRetirement`: `PolicyTemplates.RetiredAt` and `RetiredReason`, both
  nullable, no data, no byte order mark; `has-pending-model-changes` answers "no changes". No PLAN-47 trigger guards the
  template row (they guard versions and rules), so none was extended.
- **The operation `RetireDomain`** in `PolicySeedChangeSetApplier`: for the national template of a domain the catalog puts out
  of scope, a retirement draft (no rules, the operation's reason, no actor), published as `SystemSeed` through the publish
  service, which marks the template (`RetiredAt`, `RetiredReason`) in the same save. A template with an open draft is postponed
  to a later start; a governed domain named by the operation is left alone; a domain with no template is passed over with a
  debug line and no record.
- **The four sets**, appended to `PolicySeedChangeSets.All` after PLAN-47's two (which are untouched: their digests on the server
  still match, pinned by a test): `POL-049-SCOPE-RETIRE` (15 `RetireDomain`, each with a reason frozen in the set),
  `POL-049-ASSETS-ACTION` (`ReplaceRule` ×3: `R-PRIV-EVIDENCE`, `R-PRIV-WF`, `R-HIGH-APPROVAL` gain `action Equals Assign`),
  `POL-049-IDCARD-ORPHAN` (`RetireRule R-ID-PROCESS`), `POL-049-LEAVE` (`RetireRule R-LEAVE-MAX`, `R-LEAVE-LONG`;
  `ReplaceRule R-LEAVE-SELF` gains `action Equals Approve`).
- **The fresh seed**: `PolicyEngineDomainSeeds.SeedAllDomainsAsync` seeds IDCard.Lifecycle (no rule), Contracts.Leave (no rule;
  the baseline adds two, `POL-049-LEAVE` then leaves `R-LEAVE-SELF` alone), Core.Recruitment (as before) and Core.Establishment
  (as before); `PolicyEngineSeeder` seeds Assets.Custody with the three handover rules already bound to `Assign` (the same
  constants as the set, so the set finds nothing to replace). A new database: five templates, versions custody 1, card 1,
  leave 3, recruitment 2, establishment 1, and no rule in any version that the validator refuses.
- **Bindings**: a new database gets seven (the codes the catalog's triggers name); `ESTABLISHMENT-CHANGE-APPROVAL` is added
  unpinned on every database (its handler `EstablishmentChangeWorkflowHandler` exists, so the rule stays). An existing database
  keeps its thirteen and gets the fourteenth. What each old binding was for is in the baseline (0.6).
- **The start-up inventory** is logged by `NationalSeedOrchestrator` after the code policies (it ran before them on a new
  database and listed `R-PRIV-CODE` at the first start), and tells a rule in force (one warning each) from history (counted).
  Summary line: `PLAN-47 validator inventory: N rule(s) of effective versions the validator refuses; M more in superseded
  versions (history, unchanged)`. `scripts/pol047-validate.ps1` now fails unless the last start-up in the log says N = 0 with no
  rule line (checked with a good, a bad and an old-format log), and checks that the four set ids exist.
- **The page**: a retired template carries a «مسحوب» badge with its date in the list; its page says the domain is out of scope
  and why, and offers no draft and no rollback.
- **Scripts**: `seed-001-validate.ps1` looked for the text `ID-CARD-ISSUANCE` in the seeder (a binding a new database no longer
  gets); it now looks for `ASSET-ASSIGN-APPROVAL`. `est002-validate.ps1` and `hcm025-validate.ps1` are unchanged and still true.

**Tests of P2:** `Pol049ScopeChangeSetTests` (the retirement on the demo server's shape of data: final versions, marks,
history untouched rule by rule; a second and third start; a new database; an open draft; seeds that require approval; a governed
domain named by a retirement; a template that publishes rules again), `Pol049EffectiveRulesConformTests` (plan §2 rule 1, on an
old and on a new database, by the catalog directly and by the inventory; its own negative on the data of before the sets),
`Pol049FreshSeedTests`, two facts added to `WorkflowDefinitionsHaveHandlersTests` (no effective rule fires a workflow nobody
applies; the check finds `R-HIRE-START`, `R-TRF`, `R-TKT`, `R-SEP-START` before the sets and none after),
`Pol049ScopeChangeSetsPostgresTests` (three, as the app role under forced row security in the national session: new versions and
an unchanged fingerprint of every old rule, `RetiredAt` on the fifteen, 23 applications, a second and third start, two instances
at once, a new database). `Pol049LegacyPolicyData` is the fixture: the national policy data of before the plan, in the demo
server's shape, compiled into both test projects. Changed: `NationalSeedTests` (seven bindings, all governed),
`Pol049OutOfScopeTests` (the retirement marks the template), the catalog guard (the seeders may no longer name an out-of-scope
domain).

## P3 — done

- **The builders** (`PolicyDomainFactsBuilders`): `ForIDCardRequest` and `ForLeaveRequest` send no `employeeId` (no schema
  has it, and the strict evaluation refused the whole request for it); `ForLeaveRequest` takes `isSelfApproval` and sends it
  with `Approve` only; `ForEstablishmentChange` takes `actorIsBranchScoped` with no default (the fact `hasOrgScope`);
  `ForAdvanceRequest` is deleted (no caller in production or tests: `git grep` before the removal). The file names no domain
  out of scope any more, so it left the guard's short list of files that may (three files remain).
- **Leave, four channels** (HR.Contracts):
  - `CreateLeaveRequestHandler`: the tenant is the entity's (empty until the save stamps it, which the internal endpoint
    reads as "take the tenant of the call"); it was a literal `null`.
  - `UpdateLeaveRequestHandler`: asks the engine with `action=Update` and the values it would save, after the local
    validation and the "still pending" check, before anything changes; a refusal in Enforce is the same `ValidationException`.
  - `ApproveLeaveRequestHandler`: asks with `action=Approve` after the local separation of duties and before the claim, with
    `isSelfApproval` (decision 8). `ApproveLeaveRequestCommand` carries the approver's employee id, which both approve actions
    of the page take from the `employee_id` claim. A refusal is `(false, reason)`: nothing was claimed or debited.
  - `ProcessLeaveRequestedHandler` (the self-service event): computes the working days, runs `ValidateLeaveRequest`, then
    asks with `action=Create`. A local refusal (every mode) or a policy refusal (Enforce, D-POL-14) creates the request
    `Rejected`, with «مرفوض آلياً: {reason}» at the head of its notes and its service transaction cancelled as a rejection by HR
    leaves one; nothing throws, so the message is not retried and parked. A redelivered event finds the request and asks the
    engine nothing.
- **Establishment**: `SubmitForApprovalAsync` takes `actorIsBranchScoped` (no default) and passes it to the fact; both
  controllers send `HrOrgScopeVisibility.IsBranchScoped(User)` after their central-write guard, which stays.
- **The catalog** lists `InboundEventCommands.cs` as a second call site of Contracts.Leave (the registry document is
  regenerated).

**Behaviour changes recorded:** (1) a self-service leave stores its working days, not the portal's calendar days (a period over
a weekend used to store more days than the HR page would, and the balance debit followed that number); (2) a self-service
request that overlaps an approved leave, or holds no working day, is now created `Rejected` with the reason (it used to be
created `Pending` with no check). No existing test drives the self-service path, so no test data changed for it. The one unit
test of the handler now also asserts the three working days.

**Tests of P3:** `Pol049FactsBuildersMatchSchemasTests` (a builder for each of the 15 entity-type/action pairs of the catalog;
its keys are fields of the seeded schema; the engine takes each without a schema refusal; every field an effective rule reads
is sent for each action the rule can match), `Pol049LeaveChannelsTests` (16: the four channels on SQLite with the fake client,
in Shadow and in Enforce, the local validation first, D-POL-14, redelivery), `Pol049EstablishmentOrgScopeFactTests` (the real
orchestrator over the seeded rules in Enforce, Shadow and Off; the controllers; no default). Changed for a new constructor or
parameter only: `Ops038DemoDefectTests`, `OutboxAtomicityIntegrationTests`, `SoDPolicyIntegrationTests` (it now also proves the
engine is not asked when the local check refuses), `Est002…`, `EstablishmentApprovalTestHelper`, `Est024…`,
`Hcm025ApprovalCoordinatorTests`, `InboundServiceTransactionTests`.

## P4 — done

- **Modes (D-POL-1, D-POL-13).** The five governed domains are `Shadow` in HR.Core's Production and Staging files; each owner
  service sets its own domain to the same value: Admin.Logistics (`Assets.Custody`) and IDCard (`IDCard.Lifecycle`) in their
  Production file and in a new `appsettings.Staging.json` that holds the `PolicyEngine` section only, HR.Contracts
  (`Contracts.Leave`) in its Production and Staging files. The prod-like compose repeats the values on the four services
  (it runs as `Acceptance`, so the Production files are not loaded); `Assets.Custody: "Shadow"` is said explicitly on
  hr-core-web there because the lab file, part of the chain, sets it to Enforce. No production chain sets a mode in compose
  (the VPS chain included), so the Production files decide on the server. No file puts a domain in Enforce.
- **`Pol049PolicyModeConsistencyTests`** (10 cases): HR.Core and each owner agree for every governed domain in Production and
  in Staging; the merged prod-like chain gives the four services the production values; the production chains set no mode in
  compose; no shipped file or chain puts a domain in Enforce (the lab file and the three overlay files of the acceptance
  stack are the only ones allowed to) or names a domain out of scope; the start-up check accepts the files and an owner still
  needs the address of HR.Core; the resolver reads Shadow from the Production files and Off where no file applies; what
  `hcm025-validate` pins still holds. `scripts/pol049-validate.ps1` runs the class with its other tests.
- **The coordinator (G-POL-09, P-7).** New `PolicyWorkflowCodeGuard.RefuseOtherWorkflowAsync`: when the engine answers with an
  instance, the guard reads its definition's code through the national read path and compares it with the code the service
  asked for. The same code: the instance is taken, as before. Another code: the instance is cancelled the way a repair cancels
  one (status `Cancelled`, `CompletedAt`, a history entry «إلغاء (PLAN-49)» with both codes, an audit row
  `PolicyWorkflowCodeRefused` in the instance's tenant), saved in the caller's transaction, and the start is refused with
  «السياسة اختارت مسار اعتماد غير مسموح لهذا الإجراء»; the caller's change is not applied. An instance that cannot be read is
  refused too. `ApprovalStartCoordinator` and `RecruitmentAppealService` (which asks the policy and starts its review outside
  the coordinator) both go through the guard; a unit test reads HR.Core's sources and fails if any other file takes
  `policyResult.WorkflowInstanceId`.
- **Why nothing announces the cancelled instance:** `WorkflowStarter` writes the instance row and nothing else (no outbox
  message, no notification), the work inbox lists `InProgress` only, and the coordinator reuses `InProgress` only. The
  PostgreSQL test proves the unit of work from another connection: while the transaction is open none of the three rows
  (instance, ledger, audit) is visible; all three appear on commit and none on rollback.
- **Shadow ledger rows under forced row security (P-8).** `Pol049ShadowLedgerRlsPostgresTests` drives the real
  `PolicyEnforceInternalController` on PostgreSQL as a role that is neither superuser nor `BYPASSRLS`, with TEN-002C's
  `FORCE ROW LEVEL SECURITY` and PLAN-48's national read policy: an owner's Shadow call for tenant A writes one ledger row in
  tenant A (mode Shadow, the caller's client id), visible to A's session and not to B's; the two HR.Core domains do the same in
  tenant B; a body that names another tenant, a client that does not own the domain, and a domain out of scope write no row.
  This is the route chosen for P-8; the least-privilege acceptance class is optional in P5.
- **The cost of Shadow.** `PolicyEnforceClient` times the remote call: a histogram `policy_client_call_duration_ms` (tags
  domain, mode, outcome) and one line `policy_client_call domain=… action=… mode=… outcome=… elapsed_ms=…` per call (not in
  Off: there is no call). The domain and the action come from the service's own code, never from a request, so the line is
  not a log-forging source. P5 reads these lines from the owner services for AC-POL-49b and 49d.

**Tests of P4:** `Pol049PolicyModeConsistencyTests`; `Pol049CoordinatorWorkflowCodeTests` (the weak rule `ACC-R-OFF-WEAK` wins
by priority and the exception offer is refused in Enforce with nothing applied; the ordinary offer still starts; a cancelled
instance is never reused and the right workflow starts once the rule is gone; Shadow unchanged; a phantom instance refused; the
guard alone; the appeal path; external approval types get no policy instance; every taker of the policy's instance in HR.Core
goes through the guard); `S2s042PolicyEnforceClientTests` (each call is timed once with its domain, mode and outcome: `ok` in
Shadow and in Enforce, `http-503` in Shadow; Off makes no call and times nothing); `Hcm025ApprovalCoordinatorTests` (its mocked policy instance is now a real row of the requested code);
`Pol049ShadowLedgerRlsPostgresTests` (3) and `Pol049CoordinatorCancelPostgresTests` (3) on PostgreSQL.

## Decisions (with reasons)

| # | Phase | Decision | Reason |
|---|-------|----------|--------|
| 1 | P0 | The ClamAV host mapping is removed, not moved to 6350 | nothing reads it from the host; File.Storage reaches `clamav:3310` on the compose network |
| 2 | P0 | Economy mode and the session's workflow tool are reconciled as "at most two agents at a time": the inventories ran as one workflow with a concurrency of two | the prompt's rule is about load and cost, not about the tool |
| 3 | P0/P1 | At the internal endpoint an out-of-scope domain answers 400 before the ownership check (403) | the catalog says the domain has no decision to give, whoever asks; one answer for every caller |
| 4 | P0/P1 | The start-up check is registered by `AddHrProductionNetworking`, so it runs in all 15 hosts (it ran in 12) | "fails the boot of any service" (P-3) |
| 5 | P0/P1 | The validator's settings sections (`Roles`, `Seeds`, `Publish`) are skipped by the start-up check | a latent PLAN-47 defect: setting any of them stopped the boot; fixed with a test |
| 6 | P0/P1 | The finance overlay pack's leave rule (`RequireEvidence`, no consumer) becomes a custody rule bound to `Assign` | the only pack must pass the catalog; an agency rule must pass the capabilities and the ceiling |
| 7 | P0 | AC-POL-49e is adapted: the central actor's submission is the recorded one; the branch manager gets 403 and leaves no ledger row; the deny rule's match is proved by a simulation and by unit tests | both controllers refuse a branch-scoped actor before the service; letting him through would weaken a guard (see the baseline, 0.9) |
| 8 | P0 | `isSelfApproval` is `ApproverId == CreatedBy`, or the approver's `employee_id` equals the request's employee | with the plan's formula alone the fact is never true when the engine is asked (the local check refuses first), so `R-LEAVE-SELF` would be a dead rule |
| 9 | P0 | A self-service leave stores its working days, computed in HR.Contracts, and a local refusal creates it `Rejected` | the portal sends calendar days; the local validation counts working days; a consumer must not throw for a business refusal |
| 10 | P0 | A new database keeps the governed bindings only (seven with the new establishment one); existing rows stay; the establishment binding is unpinned | readers checked: PLAN-46's path reads no binding; a pin would break AC-WF-43c |
| 11 | P1 | A workflow trigger in the catalog names who starts it (`HrCoreCoordinator`, `OwnerApprovalContract`, `InternalEndpoint`); the PLAN-42 allow-list is the triggers marked `InternalEndpoint`, and there are none | the allow-list stays derived and keeps PLAN-43's meaning without a second list; a test proves it is empty |
| 12 | P1 | A `TriggerWorkflow` rule must be limited by a condition on `action` to actions whose entity type owns the code | a custody rule with no `action` condition also matches `Classify` and `Create` of type `Asset`, which do not start a handover approval: this is what `POL-049-ASSETS-ACTION` fixes in the seeds |
| 13 | P1 | The internal endpoint also checks the action against the catalog (400) | the catalog lists what each call site sends; a pair it does not list has no consumer |
| 14 | P1 | A set written before a domain left the scope (the baseline's Archive.Retention part) is recorded once as NoOp where the template exists, and passed over quietly where it does not | "the applier passes over an OutOfScope domain quietly" (P-4); the baseline set itself is unchanged, so its digest on the server still matches |
| 15 | P1 | `Integration__HrCoreBaseUrl` removed from three services only | Dev.Analytics, HR.Health and HR.Training have no other reader; the wiring check compares both ways, so an unread key must go with its registration |
| 16 | P2 | A new database is seeded already conforming (custody bound to `Assign`, no `R-ID-PROCESS`, no `R-LEAVE-LONG`), from the same constants as the sets | `Pol049FreshSeedTests` asks that every rule of a new database pass the validator; the sets then find nothing to do there, and "custody keeps one version" (a PLAN-47 assertion) holds |
| 17 | P2 | The retirement reasons are literals of the set, not read from the catalog | an applied set never changes (its digest); the catalog's wording may |
| 18 | P2 | Every out-of-scope template gets the final version, the two that never had a rule included | one shape for the fifteen: `RetiredAt` and a final published version with no rules (P7 checks exactly that) |
| 19 | P2 | With `PolicyEngine:Seeds:RequireApproval` the retirement leaves its draft for an approver, like any set; the publish service accepts that one draft in an out-of-scope domain (a version with no rules that withdraws all), from the set or from an approver, and marks the template either way | the owner's switch (D-POL-11) is honoured; nobody else can write such a draft, because the draft service opens none in the domain |
| 20 | P2 | No tenant walk for agency overrides in a retired domain | P0 found none in the seeds, the packs or the server; the engine ignores them at run time whatever the data says; the acceptance tests that created two at run time move to governed domains in P5 |
| 21 | P2 | "In force" for the inventory is the version the engine evaluates now, or one published to take effect later | the same choice the engine makes; everything else is history, which is never rewritten |
| 22 | P2 | A template that publishes rules again loses its retired mark | bringing a domain back (a descriptor, a call site, a change set) must not leave a governed template marked retired |
| 23 | P2 | The pin of `ASSET-ASSIGN-APPROVAL` to the first (now retired) definition on a new database is left as it was | nothing reads it (the handover starts through the cross-service contract); recorded as a defect for a later plan (a misleading publish warning) |
| 24 | P3 | A new leave request sends its own (still empty) tenant, not a tenant read from the session | "the tenant from the entity" (plan §4); PLAN-42's endpoint already reads an empty tenant as "not saved yet" and takes the tenant of the call, which the service client forwards from the token or from `TenantIdFlow` (the consumer); no new dependency in the handlers |
| 25 | P3 | The approval asks the engine inside the handler's transaction, right after the local separation of duties | the plan's place for it; one read of the request serves the local checks, the question and the claim, so the engine cannot decide on values the claim does not match. Cost: the transaction stays open for the call (measured with the Shadow latency in P5) |
| 26 | P3 | A self-service request refused at birth still gets its service transaction, cancelled with a reason | every leave request has one; reports and the HR page then show it exactly as a request HR rejected |
| 27 | P3 | The portal (`EssPortalController`) and the event contract are unchanged | the working days are a rule of HR.Contracts; the portal cannot call it, and the event's number is simply no longer trusted |
| 28 | P4 | One guard (`PolicyWorkflowCodeGuard`) serves the coordinator and the appeal service | the appeal review asks the policy and starts its workflow outside the coordinator; the same weakness, so the same check, and a source test keeps a third taker from appearing |
| 29 | P4 | A policy answer that names an instance nobody can read is refused | "never accepts another code": an instance whose code cannot be read is not known to be the requested one |
| 30 | P4 | **Deviation from the prompt:** `ExternalApprovalService` does not pass through the coordinator, so the check is not placed there | the prompt assumed it does. The engine starts no workflow for an external type (PLAN-43), so no policy instance can reach that service; a test proves it for a registered external type, and the cross-service start goes through the owner's approval contract |
| 31 | P4 | The audit row of the cancel carries the tenant of the instance, set by the guard | the audit table is global (`TenantEntityClassification`): nothing stamps it, and a tenant's audit view filters on its tenant. The PostgreSQL test caught the row with an empty tenant |
| 32 | P4 | P-8's route is the integration test as a non-owner role (`Pol049ShadowLedgerRlsPostgresTests`) | it runs in every CI run with Testcontainers and exercises the real controller under `FORCE ROW LEVEL SECURITY`; the least-privilege stack class would repeat it at the price of a third stack |
| 33 | P4 | The cost of Shadow is measured by the client itself (a histogram and one line per call) | the price is paid by the calling action, so the caller's clock is the right one; the line gives P5 and the server a number without a metrics backend |
| 34 | P4 | `Assets.Custody: "Shadow"` is written on prod-like hr-core-web although HR.Core's Production file would give the same | compose merges the lab file first, and that file says Enforce; without the explicit line the prod-like engine would enforce custody while the owner asked in Shadow |
| 35 | P4 | New `appsettings.Staging.json` for Admin.Logistics and IDCard holding the `PolicyEngine` section only | "Production, Staging and prod-like"; the two services had no Staging file, and a file with one section changes nothing else there |
| 36 | P4 | The timing test `S2s042GatewayProofV2Tests.One_hundred_proofs…` failed once in the full run (5.6 s against 5 s) and passes alone (54 / 54 in 9 s); left as it is | another project's containers and builds were loading the machine; PLAN-42's test, untouched here; a guard is not weakened for a local load spike |

## Files changed

- P0: `docker-compose.acceptance.yml` (ClamAV), `reports/plan49-baseline.md`, `reports/plan49-progress.md`, the prompt file,
  PLAN-48's note in `reports/plan48-progress.md`.
- P1: new `PolicyDomainCatalog.cs`, `PolicyRuleActionScope.cs` (SharedKernel), `CatalogPolicyDomainCapabilities.cs` (HR.Core),
  `scripts/pol049-validate.ps1`, three `Pol049*` test files; deleted `PolicyDomainGateway.cs`; changed: `PolicyDomainOwners`,
  `PolicyModeResolver`, `PolicyEngineConfigurationValidator`, `ProductionHostExtensions`, `IPolicyDomainCapabilities`,
  `PolicyRuleValidator`, `PolicyRuleValidationService`, `PolicyDraftService`, `PolicyPublishService`, `PolicyOrchestrator`,
  `PolicyStaticFallback`, `PolicyWorkflowTriggerAllowlist`, `PolicySeedChangeSetApplier`, `PolicyPublishedRuleInventory`,
  `AgencyProvisioningService`, `PolicyEngineDependencyInjection`, three HR.Core controllers and the policy page, eight
  `Program.cs`, `deploy/wiring/service-wiring.json`, `docker-compose.services.yml`, three k8s wiring patches,
  `docs/SERVICE_WIRING_MATRIX.md`, `docs/POLICY_DOMAIN_REGISTRY.md`, `scripts/int037-render-wiring-doc.ps1`,
  `scripts/wf043-validate.ps1`, the finance overlay pack, and the tests named in the P1 section.
- P2: new migration `20261110102522_POL049_PolicyDomainRetirement` (+ designer, snapshot), `Pol049LegacyPolicyData.cs`,
  `Pol049ScopeChangeSetTests.cs`, `Pol049EffectiveRulesConformTests.cs`, `Pol049FreshSeedTests.cs` (unit),
  `Pol049ScopeChangeSetsPostgresTests.cs` (integration); changed: `PolicyEntities.cs`, `PolicySeedChangeSets.cs`,
  `PolicySeedChangeSetApplier.cs`, `PolicyEngineSeeder.cs`, `PolicyEngineDomainSeeds.cs`, `NationalSeedOrchestrator.cs`,
  `PolicyPublishService.cs`, `PolicyPublishedRuleInventory.cs`, two comments (`PolicyDraftService.cs`,
  `PolicyRuleValidator.cs`), the two policy views, `scripts/pol047-validate.ps1`, `scripts/seed-001-validate.ps1`, the
  integration test project file (the linked fixture), and four test files.
- P3: new `LeavePolicyActions.cs` (HR.Contracts), `Pol049FactsBuildersMatchSchemasTests.cs`, `Pol049LeaveChannelsTests.cs`,
  `Pol049EstablishmentOrgScopeFactTests.cs`; changed: `PolicyDomainFactsBuilders.cs`, `PolicyDomainCatalog.cs` (a call site),
  `LeaveRequestHandlers.cs`, `InboundEventCommands.cs`, `LeaveRequestsController.cs`, `CardRequestsController.cs`,
  `EstablishmentChangeLifecycleService.cs`, the two establishment controllers, `docs/POLICY_DOMAIN_REGISTRY.md`, and nine
  test files (new arguments).
- P4: new `PolicyWorkflowCodeGuard.cs` (HR.Core), `appsettings.Staging.json` of Admin.Logistics and of IDCard,
  `Pol049PolicyModeConsistencyTests.cs`, `Pol049CoordinatorWorkflowCodeTests.cs` (unit),
  `Pol049ShadowLedgerRlsPostgresTests.cs`, `Pol049CoordinatorCancelPostgresTests.cs` (integration); changed:
  `ApprovalStartCoordinator.cs`, `RecruitmentAppealService.cs`, `PolicyClientMetrics.cs`, `PolicyEnforceClient.cs`, the
  Production files of HR.Core, HR.Contracts, Admin.Logistics and IDCard, the Staging files of HR.Core and HR.Contracts,
  `docker-compose.acceptance-prodlike.yml`, `scripts/pol049-validate.ps1`, `Pol047PostgresSupport.cs`,
  `Hcm025ApprovalCoordinatorTests.cs`, `Hcm028AppealTestSupport.cs`, `S2s042PolicyEnforceClientTests.cs`.

## Test results

| Phase | Run | Result |
|-------|-----|--------|
| P0 | build `-warnaserror` | 0 errors, 0 warnings |
| P0 | unit filter of the plan | 620 / 620 |
| P0 | integration filter, no Testcontainers | 13 passed, 22 skipped |
| P1 | build `-warnaserror` | 0 errors, 0 warnings |
| P1 | full unit suite | 5915 / 5915 |
| P1 | PostgreSQL filter `Pol047\|S2s042\|Ten048\|Hcm025` | 47 / 47 |
| P1 | integration filter `Fun002\|Est00\|RecruitmentRequisitionPolicy\|InternalChannel\|Hcm033\|IntegrationHostValidation` | 32 / 32 |
| P1 | the seven validation scripts | all PASS; `pol049-validate` fails on each of three planted defects |
| P2 | build `-warnaserror` | 0 errors, 0 warnings |
| P2 | `dotnet ef migrations has-pending-model-changes` (HR.Core) | no changes |
| P2 | full unit suite | 5943 / 5943 |
| P2 | PostgreSQL filter `Pol049\|Pol047\|Ten048\|S2s042\|Hcm025` | 50 / 50 |
| P2 | `pol049-validate` (with its tests), `pol047-validate` (static; good, bad and old logs), `wf043-validate`, `hcm025-validate -SkipTests` | PASS; the bad and the old log FAIL as they must |
| P3 | build `-warnaserror` | 0 errors, 0 warnings |
| P3 | full unit suite | 5969 / 5969 |
| P3 | integration filter (`Ops038`, `OutboxAtomicity`, `SoDPolicy`, `InboundEventConsumer`, `Est00*`, `Hcm025`, `ContractLifecycle`, `IntegrationHostValidation`, `Fun002`, …) with Testcontainers | 85 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`: it needs a built image, as before), 0 failed |
| P3 | `pol049-validate -SkipTests`, `pol047-validate`, `wf043-validate`, `hcm025-validate -SkipTests` | PASS |
| P4 | build `-warnaserror` | 0 errors, 0 warnings |
| P4 | full unit suite | 5992 / 5993; the one failure is the timing test of decision 36, which passes alone (54 / 54 of its class) |
| P4 | PostgreSQL filter of P4 with Testcontainers | 72 passed, 1 skipped (`Hcm031CompetitionScaleTests`, the 10 000-applicant scale test, which skips itself as before), 0 failed |
| P4 | `pol049-validate` (with its tests), `pol047-validate`, `wf043-validate`, `hcm025-validate -SkipTests`, `int037-wiring-check`, `vps/lint-compose`, `int037-render-wiring-doc -Check` | all PASS |
| P4 | `dotnet format --verify-no-changes` on the 14 changed C# files (Windows) | two end-of-line complaints at a comment inside an argument list of `RecruitmentAppealService.cs` (lines of an earlier plan; the known Windows artefact, the Linux container of ci-local is the judge) |

## Server changes and their undo commands

None yet (read-only so far: the running tag, the policy picture in `artifacts/plan49/vps/before-p0-policy-picture.txt`).

## Open issues

- `AcWf46PolicyOrphanTests` probably runs with hr-core-web out of Enforce (the s2s42 class recreates it without the wf46
  overlay). Not this plan's class; the pol49 overlay class asserts the container's mode itself. Recorded for the final report.
- The acceptance overrides of agencies B and C hold rules no validator would accept, on purpose (AC-TEN-48e). They stay.
  After `POL-049-ASSETS-ACTION` the national custody rules match `Assign` only, so on a `Create` or `Classify` evaluation the
  ceiling no longer has a national `TriggerWorkflow` to compare with; acceptance-only data, and no workflow starts for an
  external type whatever a rule says.
- **A defect for wave 2, before any domain goes to Enforce:** `RecruitmentAppealService` stages the reviewer's assignment
  before it asks the policy, and the orchestrator's save (or the guard's) flushes it. A refusal in Enforce (a deny rule, or
  PLAN-49's code refusal) therefore leaves the appeal assigned with no workflow, and the reviewer could decide directly.
  Latent: production runs the domain in Shadow, where nothing is refused. Not changed here (it is the appeal's own
  transaction design, outside this plan's scope); recorded for the final report.
- `AssetsController` (Admin.Logistics) reads `policyResult.WorkflowInstanceId` of the remote answer. The engine starts nothing
  for the internal endpoint (the allow-list of decision 11 is empty), so the value is always empty today; for the review.
- An ESS leave has no creator user, so `isSelfApproval` rests on the employee-id comparison there (decision 8).
  `PolicyStaticFallbackShim.GetDecimal` parses with the current culture, and `LeaveAndAdvanceRules` formats its dates with it:
  both older than this plan; for the final report.

## Exact next step

P5: the overlay `docker-compose.acceptance-pol49.yml` (Core.Recruitment in Enforce on hr-core-web, with its own seed flag for
the weak offer rule `ACC-POL49-WEAK-OFFER`, never in `PolicySeedChangeSets.All`), `scripts/pol049-outofscope-mode-boot-check.ps1`,
the group `pol49` after `s2s42` in `tests/acceptance/ci-scope.json` and `AC-POL-49` in `ac-ownership.json`,
`AcPol49EngineScopeTests` (49a..e, g on the prod-like stack) and `AcPol49EnforceOverlayTests` (49f and the boot check); move
AC-TEN-48e step 1, AC-MSG-40c and the server tour of `AcPol47VpsChecks` to governed domains; fix the stale wording of three
files; start the prod-like stack (record the time), run the plan's classes and P-11's list, read the Shadow latency lines,
`acceptance-verify`, `down -v`, restore the committed evidence; one independent review, its fixes with tests; commit P5.
