Started (UTC): 2026-10-31T22:24:14.9258001Z

# PLAN-37 progress

## P0
- status: in progress

### P0 notes (in progress)
- Preconditions:
  1. PLAN-36 merged: PR #30 MERGED 2026-10-31T22:13:29Z (merge 7d602f9c). OK.
  2. HARD-001 follow-up: PR #31 MERGED 2026-10-31T22:17:46Z (merge 19f7172b). Note: at merge time the PR's CI and CodeQL
     checks were still pending (only Gitleaks, HARD-001 scan, acceptance notice green) - the P-6 gap that this plan's
     merge rule closes. The post-merge `push` runs on main are the verdict: hardening-check success; ci in progress.
  3. main = origin/main = 19f7172b. Waiting for the ci push run 41247555134.
  4. Working tree: only the allowed items (global.json, plan36-progress.md, the prompt file, tmp-vendors.html) + this file.
  5. No other plan session on this tree (running containers are the PLAN-36 local single-host rehearsal stack).
  6. `ssh -o BatchMode=yes hrm-vps whoami` = hrm; ED25519 host key SHA256:qWIfQls882KM/nQ99yV/WFU2Wk08t6DIb8o+pUMGsto (matches).
- Decisions register: artifacts copy differs from docs/ only by 132 filled «القرار المعتمد» cells and one added header line
  (scripted line-by-line check, 0 other differences).
- Tools: kubectl present (Docker Desktop) -> k8s rendered check possible. pwsh absent.
- Server before-picture (smoke --require-integration-wiring on release 00926107a37b): pass 16, fail 0, warn 0, deferred 5:
  hr-core Payroll/FileStorage URLs and scopes; Integration__HrCoreBaseUrl for Contracts/IDCard/Archive/IT; S2S token per
  client; candidate upload to File.Storage (S2S); per-service SVC_* secrets for every client.
- 0.5: /app/seed-packs absent in hr-contracts-web and hr-movements-web images (tag 00926107a37b); present in hr-core-web.
- P0 Finished (UTC): 2026-10-31T22:32:10.8544885Z (main ci push run 41247555134 still in progress at this stamp; re-checked before P1 commit)

## P1
- status: in progress
- Decisions:
  - D1 (deviation): svc-auth-directory is NOT added to Service.HrCoreLookup: its only call (/api/lookup/employees/by-ids)
    is under HrManagementPolicies.HrCoreLookup (scope only). §1 rule 3 "no client the matrix does not justify" wins.
  - D2: allow-list entries without a call stay (removal = PLAN-42) and are listed in allowListExceptions; the reverse
    check (Allow_listed_policies_admit_only_matrix_callers) fails on any other unjustified entry.
  - D3: matrix carries an endpoints catalog (target, path, controller, serverPolicies, scope); calls[] reference it.
    Scope catalog with audiences drives the Keycloak scripts.
  - D4: inline registrations become named extensions in P3 (AddHrCoreWorkflowClient, AddDirectorySyncClient,
    AddEmployeeLifecycleGate) so the code->matrix check can name them.
- Files: deploy/wiring/service-wiring.json, TestSupport/Wiring/ServiceWiringMatrix.cs,
  UnitTests/Architecture/ProductionWiringMatrixTests.cs, scripts/int037-render-wiring-doc.ps1,
  docs/SERVICE_WIRING_MATRIX.md, docs/SERVICE_TRUST_INVENTORY.md (pointer), ServiceAuthorizationExtensions.cs
  (svc-compliance in FileStorageReferences), HR.Core.Web/Program.cs (2 unused registrations removed).
- Tests: build 0 errors; unit filter ProductionWiringMatrix|Architecture|CodeGeneration|HrGatewayConfiguration|... 137/137.
- P1 Finished (UTC): 2026-10-31T22:42:54.3111178Z

## P2
- status: in progress
- Decisions:
  - D5 (deviation from plan §2): YamlDotNet's MergingParser reports an explicit key that overrides a merged anchor as a
    duplicate key (docker-compose.production.yml does this), so ComposeEffectiveEnvironment resolves `<<` itself with
    YAML merge semantics. The rendered `docker compose config` (int037-wiring-check.ps1) stays the final judge.
  - D6: the wiring is written once in docker-compose.yml (hr-core-web, hr-contracts-web, hr-gateway) and
    docker-compose.services.yml (the rest), from the matrix; production.yml keeps ClientId + `${SVC_X:?}` secret only;
    the acceptance file keeps only the lab secret, now in its shared anchor (so the .env SVC_* values never reach the
    lab realm); docker-compose.full-stack-gateway.yml lost its duplicate gateway URLs.
  - D7: ServiceIdentity__Scope is written explicitly (also "" for Health / Dev.Analytics) so appsettings defaults can
    never widen it.
  - D8: k8s: 14 wiring-patch.yaml (all Deployments but File.Storage); gateway URLs moved from hr-yarp-config into its
    patch; staging-ha had no SVC_* keys at all, so all 13 were added (production-ha gained SVC_AUTH_DIRECTORY);
    dr-secondary uses the base placeholder Secret, which gained dev-only "replace-me" SVC_* keys.
  - D9: compose chain definitions moved to scripts/vps/compose-chains.ps1, dot-sourced by lint-compose.ps1 and
    int037-wiring-check.ps1; the unit tests read the same file (no copy).
  - D10: keycloak-sync-clients.sh (written now, run in P7) reads client secrets from the Keycloak container's own
    environment (compose fills it from .env), so no secret crosses a host command line; docker-compose.vps.yml now
    passes all 13 matrix SVC_* secrets to Keycloak.
  - D11: smoke.sh section 8 is mandatory: wiring-env per service, wiring-token per client, one signed S2S call
    (hr-core -> File.Storage service upload, 404 for a random id = authorized), legal-pack sha per image; smoke.ps1
    counts any Deferred(PLAN-*) as a failure. The bundle carries the matrix and docs/syr/service-regimes.json.
- Tests: build 0 errors; full unit suite 4388/4388; int037-wiring-check.ps1: production, single-host, prodlike, parity,
  k8s production-ha/staging-ha/dr-secondary PASS (negative run with a URL removed: FAIL naming service and key);
  lint-compose.ps1 PASS both chains.
- P2 Finished (UTC): 2026-10-31T23:06:53.3991705Z

## P3
- status: in progress
- Decisions:
  - D12: one helper for every called service: ServiceEndpoint.Require (generic) + HrCoreEndpoint (Integration:HrCoreBaseUrl,
    deprecated Services:HRCore:BaseUrl / HrCore:BaseUrl with one start-up warning via a hosted service; a disagreeing
    old key is an error). Missing outside Development = start-up error naming the key; Development outside a container
    = local default; localhost in a container = "Docker service DNS" error. Applied to every registration of the plan's
    list plus (same rule, DoD "no S2S client without a URL") the gateway (two localhost fallbacks), IT.Infra's directory
    sync (silent success removed), and Compliance's DSAR collector. Name-only AddHttpClient branches removed.
  - D13: PayrollGradeLookup / CandidateDocumentUpload keep their explicit "unavailable" implementations (PLAN-30 design;
    they register no client without a URL); the matrix and the wiring check guarantee their URLs in every chain.
  - D14: HR.Contracts' lifecycle gate: registered from the approved key (old keys warn); without any configuration (unit
    tests) still pass-through; its HTTP client has the base address but no service token (authentication = PLAN-42).
  - D15: WorkflowTriggerService signs GatewayCallerProof for /api/workflow-instances in both starts (one shared method).
  - D16: found in passing: HR.Core's hire hand-off redirected browsers to the appsettings default http://localhost:5000
    in Production (HrGateway:PublicBaseUrl unset there). Now a root-relative redirect unless the key is set; the default
    moved to appsettings.Development.json. IT.Infra's dev Auth.Directory port fixed (5140 is File.Storage; 5130).
- Tests: build 0 errors; unit 4413/4413 (new: HrCoreEndpointTests, ServiceIdentitySecretTests,
  Int037LifecycleGateRegistrationTests, Int037WorkflowTriggerProofTests, no-localhost-fallback and deprecated-key checks);
  integration 369 passed / 60 skipped / 0 failed (214 s).
- P3 Finished (UTC): 2026-10-31T23:25:13.7027026Z

## P4
- status: in progress
- Decisions:
  - D17: CodeGenerationUnavailableException derives from Exception (not in the bus ignore list). ICodeGenerationClient
    gains GenerateRequiredAsync as a default interface method (fakes keep compiling); CodeGenerationClient implements it
    with the HTTP status. ArchiveReferenceFactory.CreateReferenceAsync<TEvent> uses it, counts
    archive_reference_failures_total{event_type} (meter HRManagement.RecordsArchive, exported) and rethrows; the seven
    "reference is null -> return" sites are gone. The bus retry policy is now one public method
    (MassTransitExtensions.ConfigureMessageRetry) that the test harness uses as well.
- Tests: unit 4428/4428 (Int037ArchiveConsumerTests: 7 consumers x {throw, no row, counter} and {one row on redelivery});
  integration Int037ArchiveRetryIntegrationTests: 1 + 2 retries, Fault<ILeaveApproved>, 0 rows; replay twice after
  recovery = exactly 1 row.
- P4 Finished (UTC): 2026-10-31T23:28:58.4771730Z

## P5
- status: in progress
- Precondition 3 confirmed: main ci push run 41247555134 finished success (CI, E2E, CodeQL, Gitleaks).
- Decisions:
  - D18: the pack ships as one Content item of HRManagement.SharedKernel (output + publish); HR.Core.Infrastructure's
    copy removed. ServiceRegimePack is the only loader (configured LegalPacks:ServiceRegimesPath, then the shipped copy,
    then docs/syr only in Development); ServiceRegimeJsonPackPaths and the four duplicated search lists are gone.
    AddServiceRegimePackCheck(configuration, environment) applies the options at registration, adds the "ready" check
    and the start-up line "LegalPack loaded pack=... source=... sha256=... regimes=N separationRules=N movementTypes=N
    promotionRules=N article159Patterns=N"; a configured path that does not exist stops the host at start.
  - D19: required families: regimes, separation_rules, movement_types, promotion_rules, article_159_patterns (repository
    counts 4/4/4/4/1, so P-9 needs no stop). Repository sha256 = ad766ef68b651cbc9d718548cc66c5b394bcc7a18b12ee73ed23b932c60677da.
  - D20: scripts/syr001-validate.ps1 now checks the shared kernel's Content item (it checked HR.Core's csproj).
- Tests: unit 4439/4439 (ServiceRegimePackTests: three sources, missing configured path, fingerprint, counts, empty
  pack and missing pack Unhealthy; matrix check "every ServiceRegime*Rules reader lists the pack and registers the
  check"); integration Int037PublishOutputTests: real dotnet publish of HR.Contracts.Web and HR.Movements.Web has
  seed-packs/syr003-service-regimes-v1/regimes.json with the repository SHA-256.
- P5 Finished (UTC): 2026-10-31T23:37:29.2873197Z

## P6
- status: in progress
- Server/local state: the PLAN-36 local rehearsal stack (project hrm-vps-local, 21 containers) was stopped to free the
  Docker VM (16.6 GB) for the acceptance stack. Undo: `docker start $(docker ps -aq --filter label=com.docker.compose.project=hrm-vps-local)`.
- acceptance-up.ps1 without -NoBuild fails under PS 5.1 (missing hrm-local-dotnet-* base images; known pitfall), so the
  images are built with `docker compose ... build` directly (prod-like file list), then `acceptance-up.ps1 -ProdLike
  -NoBuild -SkipLiveVerification`. First build: 642 s, 14 images ok, hr-gateway NETSDK1064 (layer cache kept the restore
  step while the shared NuGet cache mount had lost a package) -> rebuilt hr-gateway with --no-cache.
- Image build: the BuildKit cache (~41-43 GB) is above Docker Desktop's GC limit, so GC pruned the shared NuGet cache
  mount between restore and publish (NETSDK1064, a different service per run, also with --no-cache in a parallel build).
  Fixed by building the outdated services one at a time (four with --no-cache). Build attempts: 642 s, 97 s (gateway),
  308 s, 435 s, 641 s, 277 s, 365 s; ~46 min of build time lost to this. Recorded in memory.
- Prod-like stack start (acceptance-up.ps1 -ProdLike -NoBuild -SkipLiveVerification): 199 s (220 s first start).
  The pack is in /app/seed-packs/syr003-service-regimes-v1 of hr-core-web, hr-contracts-web, hr-movements-web with
  sha256 ad766ef6...; all three log "LegalPack loaded ... source=Content sha256=ad766ef6... regimes=4 separationRules=4
  movementTypes=4 promotionRules=4 article159Patterns=1".
- Acceptance (RUN_ACCEPTANCE=true, ACCEPTANCE_REQUIRE_ALL=true), final run 2026-11-01T00:54:27Z..00:57:12Z:
  RecruitmentToHireJourneyProdLikeE2ETests Pass (19 s); AC-INT-37a..h 8/8 Pass (146 s).
- Decisions / deviations found while running:
  - D21: 37g negative answers 401, not 403: svc-hr-health's token has no matrix scope, hence no api-hr-core audience,
    and HR.Core refuses it at authentication. The test accepts 401 or 403 and checks CodeIssuances is unchanged.
  - D22: 37c issues the card through IssuedCards/Renew: IssuedCards/Create binds the IssuedCard entity, whose required
    CardDesign navigation always fails model validation (pre-existing IDCard defect, reported).
  - D23: 37d publishes ILeaveApproved for a leave created through the UI, as HR.Contracts' EventPublisher does: the UI
    approval fails on PostgreSQL (ApproveLeaveRequestHandler: ExecuteUpdate claim, then SaveChanges of the tracked
    entity against the old xmin -> DbUpdateConcurrencyException, HTTP 500). Pre-existing HR.Contracts defect, reported;
    fixing it is outside PLAN-37.
  - D24: form facts: promotion needs ToGrade > FromGrade (numbers); device status InUse; the legacy asset form must not
    post "Model" (MVC1004 binding clash, already recorded for the logistics plan); 37a picks an employee without leave or
    separation so it repeats.
- Lab (non-prod-like) stack with the shared wiring: AC-01, 02, 03, 04, 31 = 5/5 Pass (the filter also caught AC-HCM-31a/b,
  Blocked by seed state from earlier runs, unrelated; their evidence was not committed).
- P6 Finished (UTC): 2026-11-01T01:05:05.3920518Z

## P7
- status: in progress
- Server changes (with undo):
  1. 2026-11-01T01:05:38Z `/opt/hrm/shared/.env`: appended SVC_AUTH_DIRECTORY_CLIENT_SECRET (openssl rand -hex 24 on the
     server, never printed); backup `/opt/hrm/shared/.env.bak-plan37-<ts>` (600). Undo: restore the backup, or
     `sed -i '/^SVC_AUTH_DIRECTORY_CLIENT_SECRET=/d' /opt/hrm/shared/.env`, then redeploy. Now 13 SVC_* keys.
  2. Dry run of keycloak-sync-clients.sh --check from /tmp/int037 (read-only; kcadm login config inside the Keycloak
     container only). Found a SIGPIPE bug (head under pipefail), fixed in de5737d3. /tmp/int037 removed.
  3. First build-on-host (7f3ae42e4b6f) stopped by me after the fix: the remote build died after 8 of 15 images
     (local copies tagged 7f3ae42e4b6f remain in the server's image store, nothing pushed); its source archive and
     build dir were removed. Undo: `docker image rm $(docker images -q '127.0.0.1:5050/hrmanagement/*:7f3ae42e4b6f')`.
  4. build-on-host for de5737d30659 (the deployed tag): see timing below.
     build-on-host de5737d30659: 287 s (upload 18 s, build + push; server cache cleaned afterwards).
  5. Deploy de5737d30659 2026-11-01T01:36:14Z..01:38:36Z (142 s): containers healthy (pull 1 s, up 43 s, healthy 12 s),
     then keycloak-sync-clients.sh failed on the first client secret it had to set (kcadm "update -f --merge" ->
     unknown_error). Rolled back with deploy.ps1 -Rollback 01:39:07Z..01:40:27Z (80 s, old smoke 17/0). Partial realm
     changes from that run stayed (new scopes, svc-hr-movements); they are additive and the next run completed them.
     Service interruption: two container recreations (~55 s each) between 01:37 and 01:40.
  6. Fix 9d457dab: secrets set with kcadm "-s secret=" (expanded inside the container). The images built for
     de5737d30659 were tagged 9d457dab36d7 on the server registry (the only change is that deploy script; no rebuild).
     Undo: docker image rm of the :9d457dab36d7 and :de5737d30659 tags on the server (and their registry tags).
  7. Deploy 9d457dab36d7 2026-11-01T01:41:32Z..01:45:01Z (209 s): pull 2 s, up 45 s, healthy 12 s, keycloak sync 116 s
     (changes=22: 6 scopes created with their audience mappers, clients svc-hr-movements, svc-compliance, svc-hr-health,
     svc-dev-analytics created, svc-auth-directory enabled, optional scopes linked to every client, 5 secrets set);
     smoke 49 pass / 0 fail / 1 warn (external port probe inconclusive from this client: the VPN answers every port) /
     0 deferred. Second sync run: changes=0; --check exit 0. Undo of the realm changes: Keycloak console (section 7 of
     OPS_VPS_SINGLE_HOST), or keep them (additive); deploy.ps1 -Rollback returns to 00926107a37b.
- Decisions:
  - D25: docs/syr/*.json are LF in every checkout (.gitattributes): on Windows core.autocrlf=true gave the legal pack
    sha256 ad766ef6 locally vs afc1cee0 (the committed blob) on the server. The committed blob is the reference.
  - D26: AcOps36VpsBootTests re-run on the server without 36j: 36j checks the sequence "deploy two tags, then roll back"
    and expects the current release to be the last rollback target; after the PLAN-37 redeploy that is by design not
    the state. Its guarantee (rollback restores the previous tag with a green smoke) was exercised for real in step 5.
- Server proof (VPS_SSH_HOST=hrm-vps): AcInt37VpsServerTests 3/3 (mandatory smoke: wiring-env 14, wiring-token 13,
  legal-pack 3, S2S candidate upload; LegalPack line sha256=afc1cee0... in hr-core-web, hr-contracts-web,
  hr-movements-web = repository file; realm: every matrix client enabled, sync idempotent). 37a/37c NotRun(server): the
  VpsDemo realm has no acceptance personas. AcOps36VpsBootTests 36a-i, 36k: 10/10 Pass (286 s).
- P7 Finished (UTC): 2026-11-01T02:01:01.3301183Z

## P8
- status: in progress (files written earlier: scripts/ci/dedupe-push-run.sh, ci.yml dedupe job, HARD-001 step in
  steps.json + ci.yml, DedupePushRunScriptTests, CiStepsParityTests exact-gate allowance; actionlint: no findings)
- Workflows that run on push to main: ci.yml (all pushes: CI build-and-test ~53 min, E2E ~21 min, CodeQL ~23 min,
  Gitleaks ~2 min, acceptance notice), hardening-check.yml and iac-plan.yml (path-filtered, cheap: kept), deploy.yml
  (workflow_dispatch only). The guard applies to CI build-and-test (and E2E / Acceptance through needs) and CodeQL.
- Expected saving per merge whose PR run already passed on the identical tree: CI 53 + E2E 21 + CodeQL 23 = ~97 billable
  minutes (job durations of push run 41247555134); the new dedupe job costs ~1 minute on every run.
- Fail-open rules: no merged PR, a different tree, a latest PR run that is not a success, a missing artifact or any API
  error -> skip=false; a failed dedupe job also runs everything (!cancelled()).
- CiStepsParityTests now allows exactly two job keys on build-and-test: needs: [dedupe] and the fail-open if
  (compared as exact strings; any other key still fails).
- Tests: DedupePushRunScriptTests 9 cases (equal tree -> skip, different -> run, API error -> run, no PR -> run, latest PR
  run failure / in progress / missing -> run, missing artifact -> run, pull_request records the tree); actionlint no
  findings; HARD-001 scan (PS 5.1 here) exit shown above.
- P8 Finished (UTC): 2026-11-01T02:03:19.3372986Z

## P9
- status: in progress
- Resumed (UTC): 2026-11-01T02:34:41.1023770Z (context compaction while waiting for the independent review; no committed phase redone)
- Review: one independent reviewer (read-only, 19f7172b..a683af2d), 0 high, 2 medium, 7 low; all nine fixed with tests in 69ecbaa6 (no refuter: no high finding).
  Server, without a redeploy: the new keycloak-sync-clients.sh --check from a temp copy -> changes=0 (no retired or default custom scope on any client); the new smoke.sh -> 46 pass / 0 fail (--no-login), all 13 tokens and the S2S call Pass. The temp copy was removed.
  Unit 4456/4456. Rendered wiring check PASS on all chains and overlays.
- next: ci-local.ps1 on HEAD, plan035-prepush.ps1, final report, release/plan-37, PR file, owner action.
- Local CI on ecf66cde: unit 4456/4456 and MassTransit integration 344/4 skipped passed; 7 Testcontainers tests timed out connecting to PostgreSQL (Docker VM busy with the 19-container hrm-acceptance stack). Stopped hrm-acceptance (not removed). bc858c58: keycloak-sync admin login fix (stdin EOF race), server --check twice changes=0 (~90 s); stale kcadm processes of the hung runs killed inside the keycloak container (no other change).
- Final report and PR description written; next: release/plan-37 (--no-ff from main), ci-local.ps1 and plan035-prepush.ps1 on its head, owner action.
- Release head f4692d91: ci-local 90/90 passed (1010 s); plan035-prepush: actionlint, cilocal, forbidden, nonprod, signatures PASS, gitleaks FAIL: generic-api-key on this file line 17 = the server's public ED25519 host key fingerprint (not a secret). Narrow allowlist added to .gitleaks.toml (this path AND that exact fingerprint); release/plan-37 (not pushed) rebuilt from main with the fix.
- Release head 8d24700a (release/plan-37 = main 19f7172b + --no-ff merge of the feature branch at d4e53b08): ci-local full runs 3x failed only in Testcontainers (PostgreSQL connect timeouts in concurrency tests on Docker Desktop; f4692d91 with the same code passed 90/90); ci-local -SkipTestcontainers 88/88 + 2 not-run (707 s); plan035-prepush: all six checks PASS. The PR description (reports/plan37-pr.md, working tree) carries this.
- Note: `git reset --hard main` while rebuilding release/plan-37 discarded the owner's uncommitted global.json edit; restored from the unreachable blob 55f28a66 (rollForward latestPatch; dotnet 10.0.403 again). Line endings may differ from the owner's copy.
- OWNER ACTION (P9 push, PR, merge). Run from the repository root, in this order:
  1. git push -u origin release/plan-37
  2. gh pr create --base main --head release/plan-37 --title "PLAN-37: production wiring and the legal pack" --body-file reports/plan37-pr.md
  3. gh pr checks <n> --watch --fail-fast      (<n> = the PR number printed by step 2)
  4. only if step 3 exits 0: gh pr merge <n> --merge
  Then reply here (or «نفذها انت»). After the merge: main = origin/main, the post-merge ci push run (dedupe skip=true, build-and-test and CodeQL skipped) and its minutes are recorded below.
- Waiting for owner (UTC): 2026-11-01T04:17:43.2895910Z
