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

## Phase status

| Phase | Status | Finished (UTC) |
|-------|--------|----------------|
| P0 | done | 2026-11-10T10:10Z |
| P1 | done | 2026-11-10T10:20Z |
| P2 | done | 2026-11-10T10:50Z |
| P3 | pending | |
| P4 | pending | |
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

## Server changes and their undo commands

None yet (read-only so far: the running tag, the policy picture in `artifacts/plan49/vps/before-p0-policy-picture.txt`).

## Open issues

- `AcWf46PolicyOrphanTests` probably runs with hr-core-web out of Enforce (the s2s42 class recreates it without the wf46
  overlay). Not this plan's class; the pol49 overlay class asserts the container's mode itself. Recorded for the final report.
- The acceptance overrides of agencies B and C hold rules no validator would accept, on purpose (AC-TEN-48e). They stay.

## Exact next step

P3: the facts builders (`ForIDCardRequest` and `ForLeaveRequest` without `employeeId`; `isSelfApproval` on `Approve` only;
`ForAdvanceRequest` removed after a search shows no caller; `ForEstablishmentChange` takes the actor's scope with no default);
the leave handlers (`Update` and `Approve` ask the engine; `ApproveLeaveRequestCommand` carries the approver's employee id for
`isSelfApproval`; the self-service handler computes working days, runs `ValidateLeaveRequest`, asks the engine with
`action=Create`, and creates the request `Rejected` with the reason on a local or an Enforce refusal); `SubmitForApprovalAsync`
takes `actorIsBranchScoped` from both controllers; `InboundEventCommands.cs` joins the catalog's call sites; tests
`Pol049FactsBuildersMatchSchemasTests`, `Pol049LeaveChannelsTests`, `Pol049EstablishmentOrgScopeFactTests`.
