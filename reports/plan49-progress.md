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
- Resumed (UTC): 2026-11-10T17:36:39Z — the turn stopped at about 14:45Z, when a test run outlived its tool limit (the unit and
  integration runs it had started both finished, 14:39Z and 14:45Z). Nobody was at the session; the machine went to sleep
  at 15:53:17Z and woke at 17:34:06Z; the owner said «Try again» at 17:36Z, with the session's multi-agent mode switched on.
  Read this file and `git log --oneline -20` first: HEAD was still f40c5f01 (P4), P5 uncommitted in the working tree (the
  acceptance classes, the CI scope, the fixes of the independent review). Nothing remote had been touched. Waited for the
  owner: 2 h 51 min (14:45Z to 17:36Z), of which the machine slept 1 h 41 min.
- Resumed (UTC): 2026-11-10T17:47:14Z — the context was compacted in the middle of P5, while two background jobs ran (the
  local acceptance chain, started 17:40:29Z, and the second, adversarial verification of the P5 diff). Read this file and
  `git log --oneline -20` first: HEAD f40c5f01 (P4), P5 uncommitted. Nothing remote touched. No owner wait.
- Resumed (UTC): 2026-11-10T19:32:39Z — the owner restarted the machine at 19:03:44Z (Windows event 1074, a restart from the
  Start menu), in the middle of the recovery from the disk-full incident below: the last command (`docker desktop stop`, 19:03:13Z)
  was cut by it. Windows was up at 19:06:17Z, Docker Desktop at 19:09:04Z, and the owner said «اكمل» at 19:32Z. Read this file,
  the prompt and `git log --oneline -20` first: HEAD f40c5f01 (P4), P5 uncommitted, `origin/main` f99950bf, nothing remote touched.
  Waited for the owner: 29 min (19:03Z to 19:32Z), of which the restart took about 5.

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-10T10:10Z |
| P1 | done | 2026-11-10T10:20Z |
| P2 | done | 2026-11-10T10:50Z |
| P3 | done | 2026-11-10T11:20Z |
| P4 | done | 2026-11-10T12:25Z |
| P5 | done | 2026-11-11T00:45Z |
| P6 | done (nothing to merge) | 2026-11-11T00:40:48Z |
| P7 | done | 2026-11-11T01:33:05Z |
| P8 | done: PR 47 merged (merge commit `1ee60c47`) | 2026-11-11T09:13:33Z |

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
| Image build of the 16 acceptance images and the first prod-like stack start (P5, 12:30:20Z to 12:50:55Z) | 1235 s (about 12 min of build, 8 min of start) |
| Full unit suite on the P5 tree, before the review fixes (6000 tests) and after them (6008 tests) | 106 s and 157 s of tests |
| PostgreSQL and integration filter of P5 with Testcontainers (139 tests) | 289 s of tests |
| The independent review (one agent, read-only) | 31 min, beside the image build and the first acceptance scenarios |
| The local acceptance chain on the final images (2026-11-10T22:41Z to 23:52Z; five jobs through `acceptance-job.ps1`) | 71 min: image build 15 min (restore from the filled NuGet cache), stage `pol49` 20 min 31 s in all, prod-like list 14 min 33 s, `s2s42` 7 min 31 s, least-privilege 14 min 6 s (with the migrator build), lab 13 min 39 s |
| The prod-like list again after the last review fixes (23:56:52Z to 00:21:23Z, with a second image build) | 24 min 31 s (stack 948 s with the build) |
| The chain of 19:50Z that proved nothing (a failed cold image build, then stale images) | 105 min, of which 39 min in the failed build |
| The second verification (cut by the restart) and the last one (two launches, nine agents each) | about 80 min of the second before the restart; 80 and 101 min for the two launches of the last, beside the chain |
| The server: upload, build, push | 15 s, 1223 s, 17 s |
| The server: deploy to all healthy | 119.6 s (the services away about 1 min 45 s) |
| The server suites | 20 min 33 s, then three short runs (69 s, 8 s, 89 s) |
| Local CI on the first release HEAD (`ci-local -SkipTestcontainers`, `17a9076f`) | 925 s |
| The Testcontainers step on the host with the filter of CI | before the fix 4 min 42 s of tests (7 min 34 s in all: its containers started 2 min 54 s after the command); with the fix 3 min 40 s of tests (3 min 45 s in all) |
| Waiting for CI | the first PR run: about 48 min (06:46:11Z to 07:34Z). The diagnosis of its red step began at 07:14Z, when that step had failed, so about 28 min were plain waiting. The second run: 43 min 2 s (08:28:45Z to 09:11:47Z). Waited for CI in all: about 1 h 11 min |
| Local CI and the pre-push check on the HEAD with the fix (`a58e7194`) | 18 min 24 s (07:48:40Z to 08:07:04Z), then about 20 min (08:07:30Z to 08:27Z) |
| Waiting for the owner | 11 h 2 min in all, nothing asked of the owner: about 17 min (the session end of 08:43Z to 09:00Z), 2 h 51 min (the turn that stopped at 14:45Z until «Try again» at 17:36Z), 29 min (the owner's restart of the machine at 19:03Z until «اكمل» at 19:32Z), 2 h 31 min (the usage limit, 20:02Z to 22:33Z; an acceptance chain ran by itself for 93 min of it), 9 min (the turn cut at 22:48Z), and 4 h 45 min (the usage limit again, 2026-11-11 01:42Z to 06:27Z; a restart by Windows Update kept the machine down for 3 h 5 min of it) |
| The machine asleep while the session ran (not waiting for an answer) | 61 min: 13:03:12Z to 14:04:33Z, by the power button or the lid (Windows event log), in the middle of an acceptance stack start. That run failed for it: the Keycloak admin session had expired when the machine woke (`kcadm … Session has expired`; "Stack prodlike took 3849 s") |

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

## P5 — done

- **The pieces of the acceptance run (P-10).**
  - Overlay `docker-compose.acceptance-pol49.yml`: `PolicyEngine__Core.Recruitment=Enforce` and `Seed__Pol49WeakOfferRule=true` on
    hr-core-web only. The weak rule (`ACC-POL49-WEAK-OFFER`: every offer `Issue` → `RECRUITMENT-OFFER-APPROVAL`, priority 1) has
    one definition, `AcceptancePol49WeakOfferPatch`, applied by the acceptance seed only when the switch is on, through the
    change-set applier; it is never in `PolicySeedChangeSets.All` (`Pol049AcceptanceWeakOfferPatchTests`).
  - `scripts/pol049-outofscope-mode-boot-check.ps1` (ASCII, PowerShell 5.1): a one-off container of a service with
    `PolicyEngine__IT.Tickets=Shadow` must exit non-zero with the start-up check's message; the running container is untouched.
  - `tests/acceptance/ci-scope.json`: `AcPol49EngineScopeTests` in the prod-like list (group main), `AcPol49EnforceOverlayTests`
    in the group `pol49`; the stage `prodLike:pol49` has its own job `prodlike-policy` (matrix of `ci.yml`, 25 min);
    `tests/acceptance/ac-ownership.json`: `AC-POL-49` owned by PLAN-49.
- **The classes.** `AcPol49EngineScopeTests` (49a the page, the start-up inventory, no draft for a domain out of scope; 49b leave
  from the HR page, the portal, an edit and an approval, with sixty maternity days allowed and an overlapping portal request
  created `Rejected`; 49c a processed card; 49d a privileged handover with and without evidence, and `Create`/`Classify`
  without a trigger; 49e the central submission, the branch manager's 403, the deny rule by simulation; 49g drafts of agency A:
  what the catalog allows and what it refuses). `AcPol49EnforceOverlayTests` (49f: in Enforce the ordinary offer starts its
  workflow and the exception offer is refused, its policy instance cancelled, an audit row, nothing in an inbox;
  49a-boot: the boot-check script). Every ledger row is read with its facts hash, compared with the hash of what the facts
  builder sends.
- **Tests of earlier plans moved to governed domains:** AC-TEN-48e step 1 (was `Analytics.ETL`) now writes its agency rule in
  `Contracts.Leave`; AC-MSG-40c now publishes in `Assets.Custody`; PLAN-47's server tour (`AcPol47VpsChecks`) runs on
  `Contracts.Leave`. Their assertions are the same.
- **First local run** (12:30Z to 13:00Z, images of f40c5f01 with the weak-rule patch): the six scenarios of
  `AcPol49EngineScopeTests` and the two of the overlay class passed. Shadow cost on the local prod-like stack: 4 to 8 ms per
  call in steady state (the first call of a service 43 to 56 ms).
- **`scripts/acceptance-verify.ps1` cannot run here** (it needs PowerShell 7, which this machine does not have, and the plan
  forbids `pwsh`): its 32 seeded row counts were compared by psql on the fresh stack instead, 32 of 32 equal. CI runs the
  script itself.
- **The independent review** (one read-only agent, 31 min): no high finding. Eight findings, all answered:
  - F1 (medium) the start-up inventory read the national tenant only → `PolicyPublishedRuleInventory.FindAgencyOverridesAsync`
    walks every agency tenant in its own session and logs `PLAN-49 agency override inventory`; `pol047-validate.ps1` requires
    that summary and zero findings (or `-AllowAgencyOverrideFindings` on the acceptance stack);
    `Pol049OverrideInventoryPostgresTests` as a non-owner role under forced row security.
  - F2 a `TriggerWorkflow` rule could match an action that starts nothing (publishing a requisition) → a trigger of the
    catalog now names its action (`PolicyWorkflowTrigger(Code, StartedBy, Action)`), the validator and the engine ask
    `AllowsWorkflowStart` / `StartsInsideHrCore`, and inside HR.Core the engine starts a workflow only there.
  - F3 an engine outage on a portal request was recorded as a rejection → the consumer throws (the message is retried);
    F5 a retry asked for a second reference number → the number is taken first, once.
  - F4 the appeal's cancel is not one transaction with the assignment → recorded for wave 2 (open issues); the guard's
    comment says so.
  - F6 the finance pack's rule was a product rule nobody decided → it now equals the national `R-PRIV-EVIDENCE`
    (`Ten048OverlayProvisioningTests`).
  - F7 the digests of this plan's sets were not pinned → `Pol049ScopeChangeSetTests` pins the 18 of them.
  - F8 decision 3's wording was too wide → corrected below (decision 37).
- **Second verification** (the session's multi-agent mode, two agents at a time): three read-only reviewers over the P5 diff
  (the engine; the channels and the inventory; the acceptance classes and CI), two refuters for each finding, then a
  completeness critic. Its result is recorded below when it ends.
- **The first local chain** (17:40Z to 18:30Z, images of the working tree as of 17:40Z; stopped at the start of its lab run):
  - stage `pol49`: 2 of 2 (49f and 49a-boot), 20 min with the image build (stack 1156 s, of which about 12 min of build);
  - prod-like (stack 264 s): journey 1/1; main 20 of 21; `AcInt37` 8/8; `AcMsg40` 8/8; `AcWf45` 8/8; `AcWf46` 9/9; `AcPol47` 1/1.
    **The one failure was 49a:** "the agency override inventory read 0 tenant(s)". A real defect of the review fix F1, not of
    the test: at a first start the inventory ran inside the national seed, before the profile's seed created the agencies
    (decision 48). `pol047-validate -Container` passed on that stack for the same wrong reason;
  - stage `s2s42`: `AcS2s42TrustBoundaryTests` 9/9, `AcWf46PolicyOrphanTests` 1/1;
  - stage `leastpriv`: 6 of 9, three failures ("hr-core-web is not ready", "HR.Core holds no hr_core_app session"). **Not a
    defect of the code:** the stage ran with `-NoBuild`, which reuses `hr-migrate:local` when it exists, and that image was
    PLAN-48's (built 2026-11-09T20:35Z), so the database had no `RetiredAt` column. CI builds the migrator for the commit.
    The local chain now runs this stage without `-NoBuild` (it rebuilds the migrator only).
- **The second verification, as far as it has reported** (two of three reviewers; refuters and the critic still running):
  nine findings, two of them medium, none high. Medium: the outage exception was never retried (decision 49), and 49a's
  inventory assertion could not hold on a first start (decision 48, found by the chain at the same time). Low: the
  allow-list ignored the action (51); a declined start left no trace (52); the walk inside a request, and the script's
  pairing (53); a spent number on an outage (50); the test of registered external types proved the catalog's stop, not the
  engine's (it now says so, and `Pol049PolicyDomainCatalogTests` keeps every registered type out of the engine's own starts).
  All fixed with tests; the images are rebuilt once and the whole chain runs again on the final code.
- **Docs written in P5:** the «الحالة» lines of G-POL-02, 04, 09 and 11 in `docs/POL_001_GAP_STUDY.md`; "What the engine
  governs (PLAN-49)" in `docs/POLICY_ENGINE_UAT.md`; the stage and the job in `docs/CI_AND_TESTING.md`;
  `scripts/policy-inventory.ps1` rewritten (reads the catalog; `-Check`), `docs/policy-rules-inventory.csv` regenerated (65
  rows for 64 files, with «محكوم/محلي»), kept true by `Pol049RulesInventoryTests`.
- **Written for P7** (in the test project since 2026-11-10T18:40Z, committed with P7):
  `AcPol49VpsChecks.cs` (three server checks: the migration, the sets, the templates, the modes and the services without the
  client; a leave and an establishment change that each leave Shadow rows and are cancelled again; every governed domain with
  rows since the deploy, and the page). The last verification changed it before its first run (decisions 60, 61, 64).

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
| 37 | P5 | **Decision 3, corrected (review finding F8):** at the internal endpoint a service client that owns no domain is refused 403 by the endpoint's authorization policy before the action runs; inside the action a domain out of scope answers 400 before the per-domain ownership check, so the 400 is what the three owner clients see | decision 3 said "one answer for every caller"; that is true only of callers the policy lets in. Nothing changes in the code: a client with no domain never reaches the catalog, and learns nothing about it |
| 38 | P5 | **Deviation from the prompt:** the stage `prodLike:pol49` runs in its own job `prodlike-policy` and sits before `s2s42` in `order[]`, not "in the prodlike job after s2s42" | (a) `Qa066WorkflowTests` forbids a later stage on the same stack in one job after an overlay stage; (b) the PII scan runs in the job that holds the last lab/prodlike entry of `order[]` and must keep reading the stack the prod-like classes used; (c) a second stack start would pass the prodlike job's 35-minute limit (it takes 18 to 24 minutes) |
| 39 | P5 | AC-TEN-48e (step 1), AC-MSG-40c and PLAN-47's server tour move from `Analytics.ETL` to a governed domain, with the same assertions | a domain out of scope takes no draft; the tests prove the override layer, the atomic publication and the four-eyes tour, not that domain |
| 40 | P5 | The weak offer rule of AC-POL-49f has one definition (`AcceptancePol49WeakOfferPatch`), applied through the change-set applier when `Seed:Pol49WeakOfferRule` is on, and never listed in `PolicySeedChangeSets.All` | the unit test and the stack then use the same rule; no production path can seed it; a published rule is still written only by the applier |
| 41 | P5 | The acceptance classes prove "facts match the schema" by the ledger's facts hash: the hash of what the facts builder sends must equal the row's | the ledger keeps a hash, not the facts; a hash of the builder's output says exactly which facts the engine decided on |
| 42 | P5 | AC-POL-49b inserts an annual leave balance for the portal's employee by SQL when the seed gave none (test-only precondition, said in the evidence) | the seed gives balances to the first employees of the agency and for the year it ran in only; the approval debits the balance of the leave's year |
| 43 | P5 | `scripts/acceptance-verify.ps1` is replaced locally by a psql comparison of its 32 seeded counts; CI runs the script | the script needs PowerShell 7; this machine has 5.1 only and the plan forbids `pwsh` (P-12) |
| 44 | P5 | Inside HR.Core the engine starts a workflow only at the action the catalog gives HR.Core the start of (review finding F2); PLAN-42's test "a decision inside HR.Core is not limited by the allow-list" now says so, with a negative beside it | an Enforce decision for `Publish` of a requisition named `RECRUITMENT-REQUISITION-APPROVAL` would have started an instance no call site takes (G-POL-11, the same defect as R-ID-PROCESS) |
| 45 | P5 | A portal leave request whose policy question fails for an outage is retried (the consumer throws), not recorded as rejected (F3); its reference number is taken once, before the question (F5) | a rejection is a decision; an outage is not one. The employee would have been told "rejected" for a network error |
| 46 | P5 | The finance pack's custody rule is the national `R-PRIV-EVIDENCE` again, word for word (F6) | decision 6 had turned it into a rule of its own (evidence for every handover), which nobody decided; an agency pack restates or tightens what the owner decided, it does not invent |
| 47 | P5 | The session's multi-agent mode (switched on by the owner at 17:36Z) is used for a second, adversarial verification of the P5 diff, still two agents at a time | the owner asked for the most exhaustive answer; the prompt's economy rule is about load on this machine, and reading agents put none on Docker |
| 48 | P5 | The start-up inventory is taken in HR.Core's `Program.cs` after every seed of the profile (step `policy-rule-inventory`), no longer inside the national seed; it sets the national tenant itself for its first part and restores what it found | found by the acceptance run of 18:00Z: scenario 49a failed with "the agency override inventory read 0 tenant(s)". At a first start the profile's seed creates the agencies after the national seed, so the inventory read none (and `pol047-validate -Container` passed on a fresh stack without seeing agencies B and C); the acceptance seed also leaves no tenant in the flow. It ran again at every agency provisioning too (the provisioning calls the national seed). Production data was never affected: the server's tenants exist before a start |
| 49 | P5 | A self-service leave whose policy question fails for an outage now throws `PolicyEngineUnavailableException` (ServiceDefaults, derived from `Exception`), not `InvalidOperationException` | the second review read the bus's retry policy: `ConfigureMessageRetry` ignores `InvalidOperationException` unless it wraps a transient fault, so decision 45's "retried" was false: the message reached `_error` at the first failed call. `Pol049EngineOutageRetryTests` runs the policy itself: retried, consumed when the engine is back, faulted only after the last retry; the old type faulted at once |
| 50 | P5 | The reference number of a self-service leave stays first (decision 45); the comment now says what it costs | in Enforce a delivery that ends in an engine outage has spent its number (numbers are not gapless, as in the other inbound handlers); in Shadow, production's mode, an unanswered question never stops the request, so none is lost. Asking the engine first would write a second Shadow ledger row whenever the number call is retried. For wave 2, before Enforce: a stable request key on the number call |
| 51 | P5 | The internal endpoint's allow-list also carries the action a code starts at (`Allows(domain, type, action, code)`), and the engine asks it that way | the catalog's "at which action it starts" then holds on both paths; latent today (the list is empty), but the day a trigger is marked `InternalEndpoint` the defect F2 closed inside HR.Core would have been open at the endpoint |
| 52 | P5 | When the engine declines a start for a caller inside HR.Core it logs one warning (the policy version and the code: stored data, not a request's) | the answer's explanation reaches a service; inside HR.Core nobody read it, and the ledger row showed a `TriggerWorkflow` with no instance and no reason |
| 53 | P5 | The per-tenant walk of the inventory takes nothing when a session it opens is not the tenant's own; `pol047-validate.ps1` pairs the two parts of a start-up by their place in the log and fails on a part that could not be taken at the last start-up | inside a request the token's tenant comes before the flow, so the walk would read the caller's tenant once for each agency and call them all read (reachable from the onboarding request until decision 48 took the inventory out of the national seed). The script read the two parts as two unordered lists: a start-up whose national part threw passed on the numbers of the start-up before. Eleven sample logs: two pass, nine fail, each for its own reason |
| 54 | P5 | AC-POL-49a asserts the override inventory rule by rule: exactly the three pre-ceiling rules of agency B and of agency C, six in force | the second review (acceptance lens): the check only bounded what may be listed, so a walk that read three tenants and found nothing passed too |
| 55 | P5 | In a run without `-Stage`, `scripts/ci/acceptance-job.ps1` runs the PII scan on a stack an overlay stage marked, before that stack goes; the last stack is still scanned at the end; a stage job is untouched | the second review (acceptance lens): with `pol49` before `s2s42` (decision 38) a sequential run scanned a fresh stack that had carried the `s2s42` classes only. Only a person runs that path, but a guard does not get weaker for that |
| 56 | P5 | The declined start of decision 52 has a test (the warning carries the version of the ledger row and the code, once) | a refuter of the second review: every test built the engine with a null logger, so the only trace a declined start leaves inside HR.Core was unpinned |
| 57 | P5 | The local image build restores from BuildKit's cache mount, filled beforehand from the host's own NuGet cache with the 182 package versions the lock files name (`artifacts/plan49/nuget-seed/`, local only, nothing committed) | the incident emptied the build cache; the owner's line then gave about 300 KB/s with dropped connections, and the cold build failed after 34 minutes in the first service's restore. With the cache filled a restore takes about 5 s. CI and the server restore from the network as before |
| 58 | P5 | The local chain stops after its first stage when the images were not rebuilt | the chain of 19:50Z went on with `-NoBuild` after its build had failed, and its green stages said nothing about the final code |
| 59 | P5 | The national summary of the start-up inventory says how many templates it read (`...; N national template(s) read`); `pol047-validate.ps1` fails when N is 0 or the line has no count, AC-POL-49a requires at least the governed five, the server check requires all twenty | the last review (lens: the late fixes, medium): the test "the first part is read as the national tenant, whatever the caller holds" could not fail. A session that is nobody's sees no template and printed the same "0 rule(s)", so "nothing refused" and "nothing read" looked alike. The test now asserts the count; the comments no longer say the acceptance seed empties the flow (it does not: an `AsyncLocal` set inside an awaited method never reaches its caller) |
| 60 | P5 | One reader of the inventory lines for the acceptance class and for the server check (`PolicyScopeJourney.ParseStartUpInventory`): the last start-up by its place in the log, both parts, and a part that could not be taken fails | the last review (both lenses): decision 53's pairing had reached the script only; the two C# readers still took the last summary of each prefix anywhere in the log |
| 61 | P5 | The server checks of this plan: the policy page is decoded before the catalog's Arabic reasons are compared; the second check cancels what it left open (what an earlier run left, at its start, and its own, in a `finally`, by its marker and through the pages' own actions), keeps its leave inside the year that has balances, and counts the ledger rows by who asked since the check began | the last review (lens: acceptance and server; one medium, three low): the third check could not have passed on the server (Razor writes Arabic as character references, as the other server checks already know); a failure between a write and its cancel would have left an approved leave or a pending approval on the demo; from 2026-11-23 the leave would have fallen in 2027, where the demo has no balance; "nothing else was written" counted only the rows it had already counted |
| 62 | P5 | The extra PII scan of decision 55 runs only when the next stage needs a live stack | the last review (both lenses): before a stage on another stack the older block had just scanned the same stack, so it was scanned twice |
| 63 | P5 | Wording only: the cost of decision 50 is one number for each attempt of a delivery (1 + `RetryCount`), not one for a delivery; the seeder's comment points at the start-up step; the script's server recipe, `OPS_VPS_SINGLE_HOST.md` and `PROD_001_VERSIONING.md` name both prefixes | the last review (lens: the late fixes, three low) |
| 64 | P5 | The server check places its leave on the first Sunday past the notice of the annual policy (read from the server, at least seven days, one to spare); it deletes a leave it left Pending and cancels one it left Approved, and fails when a row it meant to remove is still open | the critic of the last verification (medium, both refuters could not refute it): decision 61 had moved the leave to three to six days ahead on four weekdays from 2026-11-23, and the approval refuses a leave that starts sooner than the notice (seven days in the demo seed); the page cancels approved leaves only, so the clean-up of a Pending one did nothing and said nothing |
| 65 | P5 | In `docs/policy-rules-inventory.csv` a file of HR.Core is governed only when its name states an entity the catalog lists for the domain (a requisition, an offer, a committee session, an appeal, a hire; an establishment change); the other rules of the service have no policy domain | the critic (low): every other rules file of HR.Core was mapped to `Core.Recruitment`, so competition scoring, exam marking and two indicator formulas read as governed although the engine is never asked about them. The mapping errs on the side of local now: a file whose rules sit beside an engine question under another name (the recruitment workflow rules, the legal source rules) is listed as local |
| 66 | P5 | Correction of decision 39: AC-MSG-40c published in `Training.Learning`, not in `Analytics.ETL` (AC-TEN-48e step 1 and PLAN-47 server tour were the two in `Analytics.ETL`). `docs/CI_AND_TESTING.md` now says the PII-scan sentence holds for the stage jobs and what a run without `-Stage` does. `docs/OPS_VPS_SINGLE_HOST.md` and the plan document (its `Status` and its notes) are committed in P8, with the section they point to | the critic (low): three sentences in documents were not true, or not true yet |
| 67 | P5 | Not changed, recorded for the final report: when HR.Core accepts a connection and does not answer, a Shadow call waits the client timeout of 15 s (an asset creation makes two calls) and the circuit breaker does not count that timeout; a refused connection is immediate | the critic (low), an answer to "what does Shadow add on the server when HR.Core is slow": nothing is refused and no error is added, but an action of the three owner services can take 15 s longer while HR.Core hangs. Before Enforce or a real load: a shorter timeout of its own for the policy client, and a breaker that counts timeouts |
| 68 | P5 | The rule key of the acceptance-only weak offer rule is defined as the change set id constant (one literal, the same compiled value) | the first launch of the last verification (high as reported, low to medium after two refuters, neither could refute it): a constant whose name ends in Key, set to a quoted value with digits and enough entropy, is what the pinned secret scanner reports as a generic key. It would have turned the first scan after the P5 commit red, as a look-alike did in PLAN-48, and the prompt asks to write such a literal differently rather than allow-list it |
| 69 | P5 | PLAN-47 server tour (now on `Contracts.Leave`, decision 39) discards a draft it left open, in a `finally`; its comment says that each run adds two versions to the national leave policy in force | the first launch (critic, low): on a governed template an open draft postpones every later change set of that template, and nothing cleaned up after a tour that stopped between a draft and its publication |
| 70 | P5 | New unit test: a template with a version history (five published versions, the last one in force, as the server Analytics.ETL after the tours of PLAN-47 and PLAN-48) is retired by a sixth version and every earlier version keeps its window and its rules | the first launch (critic, low): the fixture has every template on version 1, so the first start-up on the server would have been the first execution of the retirement on a multi-version template |
| 71 | P5 | Wording and small proofs from the first launch: the generated registry text and the catalog comment say a governed domain may hold no rule (the card domain holds none, and its ledger rows then name no version); the gap study limits the G-POL-09 status to the coordinator and names the open point of the appeal path; `pol047-validate.ps1 -Out` writes both parts; the retry test of decision 49 no longer waits with the test harness default idle window; two comments name the allow-list by its present shape; the server check evidence says what it counts and what it does not | the first launch (seven low findings) |
| 72 | P5 | **Deviation from decision 47 (two agents at a time):** for about fifty minutes four reading agents ran at once | the first launch of the last verification looked dead after the turn was cut at 22:48Z (its two transcripts had stopped growing) and a second was started at 22:58Z; the first had only been suspended and went on, so both ran to their end (nine agents each, the same prompts, independent answers). Their load was on the model, not on Docker; the acceptance chain ran beside them. The second answers were not wasted: the two launches agree on the two medium findings and each found things the other did not |
| 73 | P8 | The connection strings of `PostgreSqlFixture` close an idle connection after five seconds, with a test that proves it on the server; fixed on the release branch after the first run of the PR | the Testcontainers step of PR 47 failed with 53300 in `CrossTenantRlsIntegrationTests`. **A misreading before the push, recorded as it happened:** the full category had failed the same way on the host (ten tests of that class) and was put down to the many cores of the host, because the class passed alone; nothing was measured then. Measured after the red check: the collection's one server held 90 connections, 89 of them idle, 21 of them left by this plan's ten new tests, when the RLS proofs began. A higher `max_connections` was not chosen: it would hide the build-up until the collection grows again, and it would dull the one signal of a real leak. A short idle lifetime removes what finished tests leave and nothing else: a connection that is never returned is not closed by it |

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
- P5: new `docker-compose.acceptance-pol49.yml`, `scripts/pol049-outofscope-mode-boot-check.ps1`,
  `AcceptancePol49WeakOfferPatch.cs` (HR.Core seeding), `AcPol49EngineScopeTests.cs`, `AcPol49EnforceOverlayTests.cs`
  (acceptance), `Pol049OverrideInventoryPostgresTests.cs` (integration), `Pol049AcceptanceWeakOfferPatchTests.cs`,
  `Architecture/Pol049RulesInventoryTests.cs` (unit); changed: `.github/workflows/ci.yml` (one matrix entry),
  `tests/acceptance/ci-scope.json`, `tests/acceptance/ac-ownership.json`, `docker-compose.acceptance-wf46.yml` (a comment),
  `PolicyDomainCatalog.cs`, `IPolicyDomainCapabilities.cs`, `CatalogPolicyDomainCapabilities.cs`, `PolicyRuleValidator.cs`,
  `PolicyOrchestrator.cs`, `PolicyPublishedRuleInventory.cs`, `PolicyWorkflowCodeGuard.cs` (a comment),
  `AcceptanceSeedOrchestrator.cs`, `InboundEventCommands.cs` (HR.Contracts), the finance overlay pack,
  `scripts/pol047-validate.ps1`, `scripts/policy-inventory.ps1`, `docs/policy-rules-inventory.csv`,
  `docs/POLICY_DOMAIN_REGISTRY.md`, `docs/POLICY_ENGINE_UAT.md`, `docs/POL_001_GAP_STUDY.md`, `docs/CI_AND_TESTING.md`, and
  the tests `AcMsg40AtomicPublishingTests`, `AcPol47RuleLifecycleTests`, `AcPol47VpsChecks`, `AcTen48NationalScopeTests`,
  `AcWf46CrossServiceApprovalTests`, `Pol049CoordinatorCancelPostgresTests`, `S2s042PolicyLedgerCallerPostgresTests`,
  `Pol049PolicyDomainCatalogTests`, `Pol047RuleValidatorTests`, `Pol049CatalogCapabilitiesTests`,
  `Pol049CoordinatorWorkflowCodeTests`, `Pol049EffectiveRulesConformTests`, `Pol049LeaveChannelsTests`,
  `Pol049ScopeChangeSetTests`, `Qa066WorkflowTests`, `Ten048OverlayProvisioningTests`.

- P5, after the second and the last verification: new `PolicyEngineUnavailableException.cs` (ServiceDefaults),
  `Pol049EngineOutageRetryTests.cs` (integration); changed: `HR.Core.Web/Program.cs` (the step `policy-rule-inventory`),
  `NationalSeedOrchestrator.cs`, `PolicyPublishedRuleInventory.cs`, `PolicyOrchestrator.cs`, `PolicyWorkflowTriggerAllowlist.cs`,
  `PolicyEngineSeeder.cs` (a comment), `AcceptancePol49WeakOfferPatch.cs`, `InboundEventCommands.cs`,
  `scripts/ci/acceptance-job.ps1`, `scripts/pol047-validate.ps1`, `scripts/policy-inventory.ps1`,
  `docs/policy-rules-inventory.csv`, `docs/POLICY_DOMAIN_REGISTRY.md`, `docs/PROD_001_VERSIONING.md`, `docs/CI_AND_TESTING.md`,
  `docs/POL_001_GAP_STUDY.md`, `tests/acceptance/ci-scope.json`, and the tests `AcPol49EngineScopeTests`, `AcPol47VpsChecks`,
  `Pol049OverrideInventoryPostgresTests`, `Pol049CoordinatorWorkflowCodeTests`, `Pol049ScopeChangeSetTests`,
  `Pol049PolicyDomainCatalogTests`.
- Not in the P5 commit, on purpose: `AcPol49VpsChecks.cs` (P7), `docs/OPS_VPS_SINGLE_HOST.md` and the plan document (P8, with
  the section and the status they announce), `reports/plan49-final-report.md` (P8).
- P8, after the first run of the PR (release branch only, `3005a21c` and the documents commit after it):
  `src/Tests/HRManagement.IntegrationTests/TestFixtures.cs` (the two connection-string methods of `PostgreSqlFixture`), new
  `PostgreSqlFixtureIdleConnectionTests.cs`, `docs/CI_AND_TESTING.md` (the shared server's connections and two rules for a
  new test), `reports/plan49-final-report.md` (sections 2 and 6), `reports/plan49-pr.md`, the notes of the plan document.

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
| P5 | build `-warnaserror` (final tree, 2026-11-11T00:33Z) | 0 errors, 0 warnings |
| P5 | full unit suite | 6012 / 6012 |
| P5 | integration filter with Testcontainers (`Pol049`, `Pol047`, `Ten048`, `S2s042`, `Hcm025`, `Est00*`, `Ops038`, `OutboxAtomicity`, `SoDPolicy`, `InboundEventConsumer`, `IntegrationHostValidation`, `Fun002`, `Wf043`, `Wf045`, `RecruitmentRequisitionPolicy`) | 141 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, as before), 0 failed |
| P5 | `pol049-validate` (with its tests), `pol047-validate`, `wf043-validate`, `hcm025-validate -SkipTests`, `int037-wiring-check`, `vps/lint-compose`, `int037-render-wiring-doc -Check`, `policy-inventory -Check` | all PASS; `pol047-validate` on thirteen sample logs: two pass, eleven fail, each for its own reason |
| P5 | whitespace formatting of the 37 changed C# files (Windows, folder mode) | no complaint but the end-of-line artefact of Windows (4); the Linux container of ci-local is the judge in P8 |
| P5 | acceptance, stage `pol49` (AC-POL-49f and the boot check of 49a) | 2 / 2 |
| P5 | acceptance, prod-like list on the final images (the rerun of 23:56Z) | journey 1/1; main 21/21 (AC-POL-49a..e and g with it); `AcInt37` 8/8; `AcMsg40` 8/8; `AcWf45` 8/8; `AcWf46` 9/9; `AcPol47` 1/1; `pol047-validate -Container` PASS (5 national templates read, six override rules of agencies B and C, 2 of 3 tenants) |
| P5 | acceptance, stage `s2s42` | `AcS2s42TrustBoundaryTests` 9/9, `AcWf46PolicyOrphanTests` 1/1 |
| P5 | acceptance, stage `leastpriv` (migrator rebuilt) | `AcTen48LeastPrivilegeTests` 9/9 |
| P5 | acceptance, lab list | main 20 passed, 1 blocked by design (AC-09, since PLAN-42); journey 1/1; `AcHcm26` 5/5; `AcHcm27` 4/4 |
| P5 | `scripts/acceptance-verify.ps1` | not run here (it needs PowerShell 7): its 32 seeded counts compared by psql on the first stack of P5, 32 of 32; CI runs the script |
| P4 | `dotnet format --verify-no-changes` on the 14 changed C# files (Windows) | two end-of-line complaints at a comment inside an argument list of `RecruitmentAppealService.cs` (lines of an earlier plan; the known Windows artefact, the Linux container of ci-local is the judge) |
| P8 | `scripts/ci-local.ps1 -SkipTestcontainers` on `17a9076f` (the Linux container) | ok: 89 steps passed, 0 failed, 2 not run (the Testcontainers step and the coverage gate, as the switch allows); unit 6012 / 6012; the in-memory integration step 370 tests, none failed |
| P8 | `scripts/plan035-prepush.ps1` on `17a9076f` | PRE-PUSH OK: gitleaks (443 commits of history, 11 of the range), actionlint, the ci-local summary of that commit, forbidden files, non-production data, signatures |
| P8 | PR 47, first run (`42974750826` and `42974750801`, on `17a9076f`) | 13 checks green, 3 red: `CI` (the Testcontainers step: 5 failed, 205 passed, 1 skipped, of 211), `Acceptance stage (meta)` (13 passed, 2 failed: the two tests that read the result of the `CI` job) and `Acceptance` (the roll-up) |
| P8 | the Testcontainers step on the host, the filter of CI, `17a9076f` (the reproduction) | 10 failed (all in `CrossTenantRlsIntegrationTests`, 53300), 200 passed, 1 skipped, of 211 |
| P8 | `PostgreSqlFixtureIdleConnectionTests` with Npgsql's default idle lifetime (tried, then restored) | fails at its 60 s limit: "2 idle connection(s) to idle_... are still open"; with the fix it passes in 5 s |
| P8 | the Testcontainers step on the host, the filter of CI, with the fix | 211 passed, 1 skipped (`Ops038BootstrapIdempotencyTests`, as before), 0 failed, of 212 |

## Server changes and their undo commands

Changes, in the order they were made (P7, 2026-11-11):

1. **The backup** (00:41:10Z): `/opt/hrm/backups/pre-plan49-20261111T004110Z` (700; `pg_dumpall.sql` 28.7 MB, `env.backup`,
   `SHA256SUMS`: 600). Checked: 17 `CREATE DATABASE` statements for the 17 databases of the server, `hr_core_db` among them;
   the dump ends with its completion line; `sha256sum -c` OK. Undo: none needed; delete the folder when it is no longer wanted.

2. **The build** (00:42:12Z to 01:03:02Z, detached from the ssh session, with a `.done` file): the source of `046316bde9e7`
   (the P5 commit, 32.0 MB, uploaded in 15 s), build 1223 s, push 17 s, 17 of 17 images tagged `046316bde9e7` in the server's
   loopback registry; the source, the build cache and the SDK image were removed by the build script. Undo: none needed (the
   images of the previous tag are still in the registry).
3. **The deploy** (01:03:34Z to 01:05:57Z with the self-test; first attempt): `scripts/vps/deploy.ps1 -Tag 046316bde9e7`,
   119.6 s (pull 2, up 92, bootstrap 38, Keycloak reconcile 8, healthy 13); `deployments.log` says healthy at 01:05:34Z,
   previous tag 8abd5aa7fe93. The services were away for about 1 min 45 s (from the start of `up` to healthy). Self-test: 50
   pass, 0 fail, 1 warning (the port probe from the client side, as before). **Undo:** `scripts/vps/deploy.ps1 -Rollback`
   (no SQL, no restore; the rollback note below).
4. **What the first start-up wrote** (HR.Core's log, 01:05:04Z to 01:05:11Z): the migration `POL049_PolicyDomainRetirement`;
   `POL-049-SCOPE-RETIRE` on the fifteen templates out of scope (a final version with no rules each, `Analytics.ETL`'s on
   top of its five versions), `POL-049-ASSETS-ACTION` (a new version of `Assets.Custody`), `POL-049-IDCARD-ORPHAN`
   (`IDCard.Lifecycle`), `POL-049-LEAVE` (`Contracts.Leave`), the binding of `ESTABLISHMENT-CHANGE-APPROVAL`. The start-up
   inventory: `0 rule(s) of effective versions the validator refuses; 23 more in superseded versions (history, unchanged);
   20 national template(s) read`, and `0 rule(s) of overrides in force ..., in 0 of 0 agency tenant(s) read`. **Undo:** none: a
   published version is not undone (undoing one is a new version); the previous release runs beside this data.
5. **The first server check** (01:06:18Z, 69 s, Pass): the migration and the eighteen applications, the fifteen retired
   templates and their final versions, the governed versions without the withdrawn rules, `action=Assign` and
   `action=Approve` where they belong, no draft, the inventory, Shadow in the four services and no Enforce, the eight
   services without the client healthy. It writes nothing on the server.
6. **The server suites** (01:07:52Z to 01:28:26Z, 20 min 33 s, nothing heavy beside them; `artifacts/plan49/vps/suites/`): 34 of
   35 passed: `AcOps36VpsBootTests` a to k without 36j (excepted as before), `AcInt37VpsServerTests` 3/3, the server checks of
   PLAN-38, 39, 40, 43, 44, 45, 46, 47, 48, and this plan second check. The one failure,
   `AC_WF_46_vps_the_sweeps_run_and_the_metrics_exist`, read idcard-web log before its first sweep cycle (AC-OPS-39f had
   recreated every service at 01:21Z; the sweep runs every ten minutes): it passed alone at 01:32Z, as in PLAN-46..48.
   Then this plan third check, with its first again: 2/2 (01:29Z).
   **What the checks left on the server** (left as it is): from this plan, the leave `63b55b26` (22 to 24 November, approved,
   then cancelled: the balance is back at 9 days used) and the freeze request `77115597` of a vacant position (submitted, then
   cancelled with its approval instance), both marked «PLAN-49 server check», their numbers, the archive item of the
   approval, audit rows, three ledger rows; from PLAN-47 tour, versions 3 and 4 of the national `Contracts.Leave` policy
   (the rules are those of version 2); from the checks of PLAN-38 and PLAN-43..46, what they leave at every run.
7. **The ledger since the deploy** (all Shadow, each domain asked by its owner only): `Assets.Custody` 8 rows
   (`svc-admin-logistics`), `Contracts.Leave` 4 (`svc-hr-contracts`), `IDCard.Lifecycle` 1 (`svc-idcard`), `Core.Recruitment` 2 and
   `Core.Establishment` 1 (inside HR.Core); 15 rows of `Core.Recruitment` from before. The policy page shows five governed
   domains in Shadow and fifteen out of scope. **The cost of Shadow on the server** (the client clock): leave 76, 6, 14, 11 ms;
   custody 351, 13, 17, 15, 9, 13, 7, 10 ms (the first call of each container is the slow one); the card service log went with
   its container when AC-OPS-39f recreated it.
8. **At the end** (01:33Z): tag `046316bde9e7`, 20 containers healthy, the self-test with mandatory wiring 50 pass, 0 fail,
   1 warning, no draft open. The committed evidence the suites rewrote (`acceptance-evidence/`) is restored; a copy of what
   they wrote is in `artifacts/plan49/vps/suites/acceptance-evidence-from-run/`.

Read-only before it: the running tag, the policy picture of P0 in `artifacts/plan49/vps/before-p0-policy-picture.txt`.

**Read-only, 2026-11-10T18:00Z (before P7):** `origin/main` is still f99950bf; the server runs 8abd5aa7fe93 (previous
61816ed7dda2), 20 containers healthy, 56 GB free. No container has a `PolicyEngine__*` key (the Production files decide);
every .NET service except hr-core-web and file-storage-web has `Integration__HrCoreBaseUrl` today (hr-health-web,
hr-training-web and dev-analytics-web lose it with this release). The server's facts schemas of the five governed domains
hold exactly the fields the builders send, and every workflow code a governed rule names has a published definition there.

**The rollback note (P-14), from PLAN-48's code read at 8abd5aa7fe93 with `git show` (to go into `OPS_VPS_SINGLE_HOST.md`):**
- The migration adds two nullable columns to `PolicyTemplates` (`RetiredAt`, `RetiredReason`) and changes no data. The older
  model names its columns, so it neither reads nor writes them; the row of `POL049_PolicyDomainRetirement` in
  `__EFMigrationsHistory` is one the older code never looks for (it applies pending migrations only).
- The applied sets: the older applier reads the applications of its own two sets only (`POL-2026-10-30-BASELINE`,
  `POL-2026-10-30-RETIRE-R-REC-HIRE`); both are recorded on the server with the same digest, so it returns at once and
  re-adds nothing (no `R-LEAVE-MAX`, no `R-ID-PROCESS`). The four `POL-049-*` rows are never read by it.
- The retired templates and their final versions with no rules: the older engine answers a version with no rules by its
  static fallback, and no service calls those fifteen domains. The older policy page lists them as ordinary templates and
  would let an author open a draft there: do not; a draft left there must be discarded before this release returns.
- The new versions of `Assets.Custody`, `IDCard.Lifecycle` and `Contracts.Leave` stay effective; under the older code those
  domains are Off (its Production files set `Core.Recruitment: Shadow` only), so nothing evaluates them, and
  `Core.Recruitment` and `Core.Establishment` have no new version from this plan. HR.Contracts, Admin.Logistics and IDCard
  stop asking HR.Core; the ledger rows they wrote stay as history.
- The compose of the older release gives `Integration__HrCoreBaseUrl` back to the three services that lost it.
- So `scripts/vps/deploy.ps1 -Rollback` needs no SQL and no restore. The versions are not undone by a rollback (undoing one
  is a new version, never an edit); restore the backup only if data was damaged.

**P6 (2026-11-11T00:40:48Z):** `origin/main` is still f99950bf, so nothing was merged and P6 has no commit.

**Read-only before the deploy (00:41Z, `artifacts/plan49/vps/before-p7-policy-picture.txt` and `before-p7-modes.txt`):** the
running tag 8abd5aa7fe93 is on `origin/main`; 20 of 22 containers report healthy (the two others have no health check), 56 GB
free. The templates, versions and rules are those of the P0 picture, line for line; no draft is open; the ledger holds 15 rows,
all `Core.Recruitment` in Shadow asked inside HR.Core. The four services run as Production with no `PolicyEngine__*` key in
their environment; only hr-core-web has a `PolicyEngine` section in its Production file (`Core.Recruitment: Shadow`); all eleven
services that had the policy client still have `Integration__HrCoreBaseUrl`. What the server check will need is there: the
legal source it names, 75 vacant positions with no open request, the published establishment definition, 425 annual balances of
2026 with ten days or more, and the four demo users.

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

None for this plan: PR 47 is merged and the merge note below is written. The next plan brings the local branch
`docs/plan-49-merge-note` in (one commit on top of `1ee60c47`), as this plan did for the note of PLAN-48.

## Incident: the system disk filled and the Docker engine stopped (2026-11-10T18:40Z)

- **What happened.** The second local chain started its image build at 18:39Z. At 18:40:41Z drive C had 0 bytes free;
  `dockerd` and `initd` inside Docker Desktop's VM died with SIGBUS (two crash dumps in `%TEMP%\wsl-crashes`), the stage
  `pol49` and the runs after it failed at once, and a test run on the host ended with "No space left on device". Docker's
  data disk (`docker_data.vhdx`, 245.3 GB, not sparse) is on C; the rest of C's free space was taken by the Windows page file
  under memory pressure (the image build, the test runs and another project's builds at once): 16 GB came back by itself
  within minutes of the chain stopping.
- **This plan's part in it.** The image builds of today grew the virtual disk, and the least-privilege stage of 18:22Z
  (decision: the stale migrator image) left hr-core-web in a crash loop, whose eight dumps (2.7 GB) the VM wrote to
  `%TEMP%\wsl-crashes`.
- **What was done, and nothing else.** The two chain processes were stopped. The eight dumps of this repository's test
  containers were **moved** (not deleted) to `artifacts/plan49/wsl-crashes/` on drive D; the two dumps of Docker itself stay
  where Docker wrote them. No file of the owner or of another project was deleted or moved. C had 23 GB free at 18:46Z.
- **Next:** the engine does not answer (its API hangs), so Docker Desktop is restarted with its own command
  (`docker desktop restart`); then only this repository's leftovers are removed inside Docker (the `hrm-*` projects and
  their volumes, the images of earlier builds), and the free space of C is checked before every image build from here on.
- **What followed (18:53Z to 19:40Z).** `docker desktop restart` failed ("Docker Desktop terminated unexpectedly"), and
  `docker desktop start` could not mount the data disk (`/dev/sde is not usable: device or resource busy`: the WSL machine
  of the crashed engine still held it). The owner then restarted Windows (19:03:44Z); Docker Desktop came up by itself with
  the data disk intact (143 images, the other projects' containers running). No `hrm-*` container or volume was left.
  The build cache was damaged (`docker system df` and `docker buildx du` failed on a snapshot the crash never finished
  writing) and was emptied by Docker itself within minutes of the start (177 MB at 19:39Z; nothing was pruned by hand).
  Removed by hand, and nothing else: 74 untagged images of this repository's earlier builds (each checked: its entry point
  is this repository's `APP_DLL`, no tag, no container). Inside the data disk the used space went from 242 GB to 40 GB, so
  new layers reuse freed blocks and the disk file on C (245.3 GB) need not grow. C had 35 GB free at 19:40Z.
  Left alone: the images `repo-*` (this repository's services, built 2026-11-09/10 by a compose project named `repo`, of
  unknown origin), `hr-rabbitmq`, the two exited RabbitMQ containers, every image and volume of the other projects.
- **From here on:** one heavy job at a time (never an image build beside a host test run); a watchdog
  (`artifacts/plan49/disk-watch.ps1`) logs C's free space every minute and stops this plan's own run below 8 GB. The next
  image build is cold (the NuGet cache went with the build cache).
- **For the owner (not done by the agent, a setting of the machine):** Docker's data disk (245 GB) sits on C, which has
  35 GB free of 457; drive D has 262 GB free. Moving the disk image to D (Docker Desktop, Settings, Resources, Disk image
  location) or compacting it would remove the cause.

## Checkpoint: the turn ended at the usage limit of the session (2026-11-10T19:59:51Z)

- **The final tree, checked before the chain:** build `-warnaserror` 0 errors, 0 warnings; unit 6011 / 6011
  (`artifacts/plan49/p5b/unit-final.log`); the integration filter with Testcontainers 141 passed, 1 skipped, 0 failed
  (`integration-final.log`, 220 s). After two more test edits: `Pol049CoordinatorWorkflowCodeTests|Qa066` 142 / 142, and the
  acceptance project builds.
- **The second verification** (workflow `wf_95877ac7-863`, cut by the restart; what it returned is in
  `artifacts/plan49/second-verification/journal-results.txt`): the engine lens (4 low) and the channels lens (2 medium,
  3 low) were all fixed already (decisions 48 to 53; the eight refuters that ran say so). The acceptance and CI lens:
  (1) high, 49a on a first boot: the running chain is its proof; (2) low: 49a now asserts the six seeded pairs of agencies
  B and C exactly; (3) low: in a run without `-Stage`, `scripts/ci/acceptance-job.ps1` now scans a marked stack before it
  goes (the stage jobs are untouched), and the description of `ci-scope.json` says so. Also: the declined-start warning of
  decision 52 has a test, and a stale test comment is corrected. **Not run:** the refuters of the acceptance/CI and
  channels findings, and the completeness critic.
- **Running in the background when the turn ended** (started 19:50:54Z): `bash artifacts/plan49/run-acceptance-chain.sh all`
  (a cold image build first), the disk watchdog (`artifacts/plan49/disk-watch.log`), and a waiter that brings this
  repository's stacks down when the chain ends (`artifacts/plan49/acceptance/final-reset.log`).
- **Next:** read `artifacts/plan49/acceptance/chain.log`; rerun a red stage alone; then steps 4 and 5 of "Exact next step"
  (the validation scripts, `policy-inventory -Check`, restore the test outputs, commit P5, gitleaks), then P6, P7, P8.
  `AcPol49VpsChecks.cs` is already in the test project (it is committed with P7).

- Resumed (UTC): 2026-11-10T22:33:50Z — the turn of 19:32Z ended at 20:02Z at the usage limit of the owner's plan, with the
  acceptance chain running in the background (it ended by itself at 21:35:27Z and its stacks were brought down at 21:35:35Z).
  The owner said the limit had reset. Read this file first; HEAD f40c5f01, P5 uncommitted, nothing remote touched.
  Waited for the owner: 58 min of idle time after the chain ended (21:35Z to 22:33Z); the 93 minutes before that were the
  chain's own run.
- **The chain of 19:50Z to 21:35Z proved nothing about the final code.** Its image build was cold (the build cache was lost in
  the incident) and failed in the first service's `dotnet restore` after 34 minutes: `Failed to download package ... from
  api.nuget.org: Received an unexpected EOF` (the owner's network). No service image was rebuilt, so the later stages
  (`-NoBuild`) ran on the images of 17:45Z and 18:39Z: 49a failed again for the reason decision 48 fixes (HR.Core's image
  still took the inventory inside the national seed), and the green stages (s2s42 10/10, leastpriv 9/9 with a rebuilt
  migrator, lab 30 passed and 1 skipped) are results of stale images. Disk: C never fell below 23.9 GB.
- Resumed (UTC): 2026-11-10T22:57:56Z — the turn was cut at about 22:48Z (not by the session: no tool call was running, the
  machine did not sleep) and the owner said «Try again» at 22:57Z. The acceptance chain started at 22:41:10Z kept running in
  the background (its fifteen images were all rebuilt this time); the read-only verification workflow was killed with the
  turn before any reviewer had answered and was started again. Waited for the owner: 9 min.
- **The chain of 22:41Z, on fifteen rebuilt images** (`hr-core-web` built 22:49Z from the final tree; image build 15 min with
  the filled NuGet cache; the first start-up of HR.Core on a new database logged the national inventory 0 and "6 rule(s) of
  overrides in force ..., in 2 of 3 agency tenant(s) read", which is what decision 48 was for):
  - stage `pol49` (22:41:10Z to 23:01:41Z, stack 1174 s with the build): 2 of 2 (49f and the boot check of 49a);
  - prod-like (23:01:49Z to 23:16:22Z, stack 306 s): journey 1/1; main 21 of 21, **49a with the rule-by-rule assertion
    included**; `AcInt37` 8/8; `AcMsg40` 7 of 8; `AcWf45` 8/8; `AcWf46` 9/9; `AcPol47` 1/1; `pol047-validate -Container` PASS
    (eight start-ups of HR.Core in the log, each with its two parts).
  - **The one failure, `Ac40f_no_unsaved_outbox_message_in_any_of_the_thirteen_services`** (PLAN-40's test, lines this plan
    did not touch): one of its thirteen plain `GET /metrics` calls through the published ports ended after 31 s with "An
    existing connection was forcibly closed by the remote host". HR.Core did not restart then (its start-ups are in the log:
    23:11:03Z, then 23:13:27Z), the VM had no out-of-memory kill, and a minute later all thirteen endpoints answered in 0.15 s.
    Docker Desktop's own log shows what else was happening: from 23:10:00Z another project's test run (a .NET 8 container,
    `p9-full`) was creating and deleting Testcontainers containers with published ports every few seconds (a PostgreSQL one,
    then RabbitMQ ones at 23:11:42Z, 23:12:00Z, 23:12:08Z, ...), each with `exposer.Add` / `exposer.Remove` on the same port
    forwarder this request went through. The review agents ran no `dotnet` and no `docker` (their transcripts were
    searched). Read as an environment failure; the class is rerun alone after the chain to make sure.
  - stage `s2s42` (23:16:39Z to 23:24:10Z): `AcS2s42TrustBoundaryTests` 9/9, `AcWf46PolicyOrphanTests` 1/1;
  - stage `leastpriv` (23:24:12Z to 23:38:18Z, with the migrator rebuilt for this tree): `AcTen48LeastPrivilegeTests` 9/9;
  - lab (23:38:23Z to 23:52:02Z): main 20 passed and 1 blocked by design (`Ac09ShadowPolicyTests`: "the mode is derived by the
    server since PLAN-42 ... PLAN-65 rewrites this scenario", as before this plan); journey 1/1; `AcHcm26` 5/5; `AcHcm27` 4/4.
  - Disk during the chain: C between 30.4 and 31.7 GB free.
- **The last verification** (the session's multi-agent mode, read-only, two agents at a time; the first launch was killed with
  the turn at 22:48Z and it was started again at 22:58Z): two reviewers over what nobody independent had read (the fixes
  written after the second review; the server-check class and the last acceptance edits), then a critic. The two reviewers:
  no high finding, two medium, ten low (two of them the same), all fixed (decisions 59 to 63):
  a test that could not fail and a summary line that could not tell "nothing refused" from "nothing read"; a server check
  that compared Arabic text with an undecoded page; the server check's clean-up, its dates and its ledger count; the pairing
  of the two inventory parts in the C# readers; a double PII scan; three wordings. Nothing was run by the reviewers.
  After the fixes: build `-warnaserror` 0/0; unit `Pol049|Pol047|Qa066|Ten048` 501/501; PostgreSQL
  `Pol049|Pol047|Ten048|S2s042` 47/47; the stricter inventory test fails when the national-tenant set is removed (tried,
  then restored); `pol047-validate.ps1` on thirteen sample logs: two pass, eleven fail, each for its own reason.
  The images are built again for the changed line of HR.Core and the prod-like list runs once more (it also reruns the
  class of AC-MSG-40f).

## P8 — the pull request, its first run and the fix (2026-11-11)

- Resumed (UTC): 2026-11-11T06:27:25Z — the turn that wrote P7 and the P8 documents ended at about 01:42Z at the usage limit
  of the owner's plan, with `scripts/ci-local.ps1 -SkipTestcontainers` running on the release HEAD. It finished green by
  itself at 01:54Z; an automatic wake-up at 01:55Z read its result and pushed nothing, because nobody had spoken. Windows
  Update restarted the machine at 03:18:05Z (event 1074, `MoUsoCoreWorker.exe`); the event log starts again at 06:21:11Z, a
  second restart for servicing follows at 06:21:33Z, and Windows is up at 06:23:21Z. The owner said «اكمل» at 06:27Z. Read
  this file, the prompt and `git log --oneline -20` first: `release/plan-49` at `17a9076f`, the tree clean but for the
  owner's `global.json`, `origin/main` still `f99950bf`, the server on `046316bde9e7` with its 20 containers healthy. Docker
  Desktop was not running after the restart: the first `docker desktop start` failed on a stale socket of its secrets
  engine, and the engine was up at 06:29Z. Waited for the owner: 4 h 45 min (01:42Z to 06:27Z); the machine was down for
  3 h 5 min of it.
- Resumed (UTC): 2026-11-11T07:17Z (to the minute) — the context was compacted while the checks of the PR were being
  watched; the `CI` check had just turned red (07:14Z). Read the summary and this file first. No owner wait.
- **Local gates on `17a9076f`:** `ci-local -SkipTestcontainers` ok (89 steps passed, 0 failed, 2 not run; 925 s; the summary
  names that exact commit and no dirty tracked file); `plan035-prepush.ps1` ended at 06:44Z with PRE-PUSH OK.
- **Push and PR:** `git push -u origin release/plan-49` at 06:45:45Z; PR 47 opened at 06:46:00Z
  (https://github.com/omardefdev/HRManagement/pull/47). Runs `42974750826` (`ci`) and `42974750801` (`hardening-check`)
  started at 06:46:11Z. The stage jobs run side by side since PLAN-66, so the whole run ended at about 07:34Z.
- **The first run, check by check.** Green: Gitleaks (1 min), Dedupe post-merge run (7 s), Reuse check (33 s), HARD-001
  compliance scan (2 min 18 s), E2E (11 min 40 s), CodeQL (14 min 16 s), Acceptance images (18 min 56 s), and six
  acceptance stages: `leastpriv` (8 min 47 s), `prodlike-policy` (9 min 12 s; AC-POL-49f and the boot check, on CI for the
  first time), `lab-rest` (17 min 13 s), `prodlike` (21 min 38 s), `lab-main` (26 min 20 s), `prodlike-final`
  (27 min 38 s). **Red:** `CI` (26 min 33 s), in the step "Integration tests (Testcontainers)": 5 failed, 205 passed,
  1 skipped, of 211, every failure in `CrossTenantRlsIntegrationTests` with `53300` ("sorry, too many clients already",
  "remaining connection slots are reserved for roles with the SUPERUSER attribute"); `Acceptance stage (meta)`
  (10 min 53 s): 13 passed, 2 failed, the two tests that read the result of the `CI` job (`AcQa34PushableBaselineTests`:
  "Failed step 'Integration tests (Testcontainers)'", and `AcQa35RemoteCiTests`); and `Acceptance`, the roll-up.
  One cause for the three.
- **The cause, measured.** The full step ran on the host with the filter of CI while a sampler read `pg_stat_activity` of
  the run's own PostgreSQL containers every few seconds (`artifacts/plan49/tc-host/baseline/`, local): 10 failed, 200
  passed, 1 skipped, of 211, the same class and the same error. The server of the `PostgreSql` collection climbed without a
  fall, from 1 connection to 90 in 2 min 43 s (07:27:28Z to 07:30:11Z), and at 90 it held 89 idle ones and one in a
  transaction: one to three for each database of each test that had finished, 64 of the superuser and 26 of the app role.
  21 of the 90 belonged to the ten tests this plan added (`pol047_p49...`, ten databases). The RLS proofs take eighteen
  databases, most of them with an owner connection and an app connection, and the server has 100 slots, three of them
  reserved.
  Nothing leaks: every test takes a database of its own, so a pool of its own, and Npgsql keeps an idle pooled connection
  for five minutes (`Connection Idle Lifetime`, 300 s). Without this plan's tests the same moment would show 69 on this
  host: the collection was already close to its limit, and the ten new tests crossed it.
  The server of the `PgRabbit` collection stood at 81 in the same run: it was the next to fail.
- **The fix (`3005a21c`, decision 73).** `PostgreSqlFixture.CreateDatabaseAsync` and `CreateAppConnectionAsync` return
  connection strings with an idle lifetime of 5 s and a pruning interval of 1 s; a string a test derives from them (another
  user, for instance) keeps both. New test `PostgreSqlFixtureIdleConnectionTests`: it holds one owner and one app
  connection (the server shows 2), gives them back, and waits for the server to show none; it fails at its 60 s limit with
  Npgsql's defaults (tried, then restored) and passes in 5 s with the fix. `docs/CI_AND_TESTING.md` says why, and gives two
  rules for a new test.
- **After the fix, same host, same filter** (`artifacts/plan49/tc-host/fixed/`): 211 passed, 1 skipped, 0 failed, of 212,
  in 3 min 40 s (4 min 42 s before). The shared server's peak: 25, all idle, at the end of the RLS class (its own
  databases); it stayed under 10 until that class. The `PgRabbit` server's peak: 11.
- **What was misread, and the rule it leaves.** Before the push the same ten failures had shown in the host run of the full
  category, and they were put down to the many cores of the host because the class passed alone (18 of 18). A class that
  fails in the full step and passes alone is a real failure: the server is shared, and CI runs the whole step. The local
  CI of that night could not catch it (the VPN keeps its container from Testcontainers, so that step was "not run"), which
  is why the host run of the full step is the gate when the switch `-SkipTestcontainers` is used.
- **Not done for the first run:** no job was rerun. The failure is not from the environment (it shows on the host too, and
  it depends on how fast the tests before the RLS class run), so a rerun that happened to pass would have proved nothing.
  Nothing was merged, and the demo server was not touched: the change is in the test project and one document.

## Merge note (2026-11-11)

- **The local gates on the HEAD with the fix (`a58e7194`):** `ci-local -SkipTestcontainers` ok (89 steps passed, 0 failed,
  2 not run; unit 6012 / 6012; the in-memory integration step 370 tests, none failed; summary generated 08:07:04Z, no dirty
  tracked file); `plan035-prepush.ps1`: PRE-PUSH OK (gitleaks 445 commits of history and 13 of the range, actionlint,
  the ci-local summary of that commit, forbidden files, non-production data, signatures). The Testcontainers step had run
  on the host with the fix: 211 passed, 1 skipped, 0 failed, of 212.
- **The second push:** `git push origin release/plan-49` at 08:28:39Z (`17a9076f..a58e7194`; the push hook: PRE-PUSH OK).
  The description of the PR was updated from `reports/plan49-pr.md`. `origin/main` was still `f99950bf`.
- **CI run `42987351533` on `a58e7194`** (pull_request; with `42987351570`, `hardening-check`), 08:28:45Z to 09:11:47Z:
  **16 of 16 green.**
  - `CI` 31 min 25 s: unit 6012 / 6012; the in-memory integration step 366 passed and 4 skipped, of 370; **the
    Testcontainers step 211 passed, 1 skipped, 0 failed, of 212, in 6 min 22 s** (5 failed in 7 min 58 s in the first run);
    the coverage gate 91.6 % (12169 of 13290 lines; floor 60 %).
  - E2E 15 min 35 s; CodeQL 14 min 23 s; Gitleaks 1 min 2 s; Reuse check 25 s; Dedupe post-merge run 7 s; HARD-001
    compliance scan 1 min 43 s; Acceptance images 14 min 46 s.
  - Acceptance stages: `lab-main` 25 min 49 s; `lab-rest` 13 min 38 s; `prodlike` 20 min 32 s; `prodlike-policy`
    11 min 17 s; `prodlike-final` 26 min 55 s; `leastpriv` 8 min 45 s; `meta` 18 min 53 s (`AcQa34PushableBaselineTests`
    8 of 8 and `AcQa35RemoteCiTests` 7 of 7: the `CI` job they read was green this time).
  - `Acceptance` (the required roll-up), 36 s: PASS. 81 classes, 276 tests passed. 4 failed: the four payroll classes
    marked not required in `ci-scope.json` (`Ac21`, `Ac23`, `Ac24`, `Ac25`), as in the green runs of PLAN-47 and PLAN-48.
    2 blocked: `Ac09` (by design since PLAN-42) and `Ac12` (it needs a payroll run). **This plan's classes:
    `AcPol49EngineScopeTests` 6 of 6 (prod-like, group main: AC-POL-49a..e and g) and `AcPol49EnforceOverlayTests` 2 of 2
    (stage `pol49`: AC-POL-49f and the boot check of 49a).** The prod-like stacks in all: 18 classes, 94 tests, none
    failed; beside this plan's two: `AcHcm25` 8, `AcInt37` 8, `AcMsg40` 8, `AcOps38` 8, `AcOps39` 8, `AcPol47` 1, `AcQa41` 4,
    `AcS2s42` 9, `AcTen48LeastPrivilegeTests` 1, `AcTen48NationalScopeTests` 1, `AcWf43` 6, `AcWf44` 5, `AcWf45` 8,
    `AcWf46` 9, `AcWf46PolicyOrphanTests` 1 and the prod-like journey 1.
  - Evidence of record: the `acceptance-evidence` artifact, https://github.com/omardefdev/HRManagement/actions/runs/42987351533/artifacts/13842207523
    (kept until 2026-12-11).
- Merged by agent on owner instruction (UTC): 2026-11-11T09:13:33Z. PR https://github.com/omardefdev/HRManagement/pull/47
  was merged with a merge commit (`gh pr merge 47 --merge`) at head `a58e7194`, every one of the 16 checks green, the merge
  state CLEAN and `origin/main` still `f99950bf` (no other PR had merged first). Merge commit:
  `1ee60c47490b1b1a3f73ebe2fefe64908b0e3b03` ("Merge pull request #47 from omardefdev/release/plan-49"). The remote branch
  `release/plan-49` is kept. The side PRs 40 and 42 and their branches were not touched.
- `git switch main`; `git pull --ff-only`: `main` = `origin/main` = `1ee60c47490b`, verified. This note is committed on the
  local branch `docs/plan-49-merge-note`, cut from that `main`: not pushed, no second PR; the next plan brings it in.
- **Post-merge runs** on `main` at `1ee60c47` (push): `42993370773` (`ci`), 09:13:35Z to 09:14:40Z, success: the dedupe job
  found that the PR run had tested this tree, so only Gitleaks ran, and the build, CodeQL, E2E and the acceptance jobs were
  skipped by design (PLAN-66); `42993370689` (`hardening-check`): success.
- WAITING-CI: none (PR 47 merged).
- **Timing:**
  - Start 2026-11-10T08:35:40Z. First push 2026-11-11T06:45:45Z, 22 h 10 min after the start. Merge 09:13:33Z. Total
    wall-clock 24 h 38 min; the session stood still for 11 h 2 min of it without anybody being asked anything (six stops,
    in the durations table), which leaves about 13 h 36 min of session time.
  - From the first push to the merge, 2 h 28 min: 28 min until the red step of the first run showed (07:13Z); 1 h 15 min
    from there to the second push (08:28:39Z): the job log and the measurement on the host (07:17Z to 07:32Z), the fix and
    its proof (to 07:41Z), the documents and the two commits (to 07:48Z), local CI (18 min), the pre-push check (20 min);
    43 min for the second run; 2 min from green to the merge. CI ran 1 h 31 min in all (47 min 55 s, then 43 min 2 s).
  - Waited for CI: about 1 h 11 min (28 min of the first run before its red step showed, and the second run; the server
    was checked and the figures of this note were gathered beside it).
  - Actions: run 1 197.6 runner-minutes (205 billed, each job rounded up); run 2 205.8 (213 billed); the two post-merge
    runs 3.2 (6 billed).
  - `scripts/plan-timing.ps1 -From 25 -To 49` after the merge: PLAN-49 10 commits, started 2026-11-10 11:35, first commit
    11:37, end 2026-11-11 04:35 (Riyadh time), span 16:58, wall-clock 17:00. The script takes the merge of the feature
    branch into `release/plan-49` (`17a9076f`, 01:35Z) for the merge, so its figure stops before the PR, as for PLAN-48.
- **Demo server, checked after the merge** (09:19Z): 22 containers up, the 20 with a health check all healthy, the 16
  service images tagged `046316bde9e7`; the public site answers 302 to the login. The server runs the product code of
  `main`: between `046316bd` and the merge commit only tests, documents and reports changed (nothing under `src` outside
  `src/Tests`, nothing under `docker` or `scripts/vps`), so nothing was deployed after the merge.
- **Seen beside the plan, not part of it:** the nightly run of 2026-11-10 on the old `main` (`42791270343`) failed in
  "Verify Docker builds" on a network error of its runner (apt index files failed to download inside an image build), with
  the `meta` stage and the roll-up behind it. Both PR runs of this plan passed that step. The nightly of 2026-11-11
  (`42991787350`) started at 09:02Z on the old `main`, eleven minutes before the merge.
- **For the owner** (also in the final report): Docker Desktop keeps its data disk on drive C, which had about 28 GB free
  during this plan and filled once; moving it to drive D (about 250 GB free) is a setting of the owner.
