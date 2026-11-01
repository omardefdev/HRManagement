# PLAN-37 final report — production wiring, the wiring matrix and the legal pack

Plan: [PLAN-37](../docs/acceptance-plans/PLAN-37-int-production-wiring-legal-pack.md) · prompt:
[PLAN-37-AGENT-PROMPT.md](../docs/acceptance-plans/execution/PLAN-37-AGENT-PROMPT.md) · checkpoint:
[plan37-progress.md](plan37-progress.md) · baseline: [plan37-baseline.md](plan37-baseline.md).

## 1. Summary per phase

| Phase | Result |
|---|---|
| P0 | Preconditions met (PR #30 and #31 merged; main = origin/main; main's post-merge `ci` run 41247555134 green after the P0 stamp; ssh `hrm`, host key matches). Decisions register: 132 cells filled + one header line, nothing else. Baseline: build 0/0, unit filters 204/204; code inventory vs the plan's §1 table; effective environment of production, single-host, acceptance, prod-like and three k8s overlays; `/app/seed-packs` absent in HR.Contracts and HR.Movements images; server smoke: 16 pass, 5 `Deferred(PLAN-37)`. |
| P1 | `deploy/wiring/service-wiring.json` (15 services, 13 service clients, 8 scopes, 15 endpoints, 13 registrations, 33 calls), `ServiceWiringMatrix` loader + schema, rendered `docs/SERVICE_WIRING_MATRIX.md` (P-8 contract inside), `svc-compliance` in `Service.FileStorageReferences`, two unused HR.Core registrations removed; every call evaluated against the real policies (positive and negative) and the allow-lists checked in reverse. |
| P2 | Wiring written once in `docker-compose.yml` / `docker-compose.services.yml` from the matrix; production file keeps client id + `${SVC_X:?}`; acceptance files carry only the lab secret; 14 k8s `wiring-patch.yaml`; Keycloak scripts read the matrix; `acceptance-up.ps1 -ProdLike`; mandatory wiring smoke; `int037-wiring-check.ps1` (rendered compose + `kubectl kustomize`); `ProductionWiringMatrixTests` six checks. |
| P3 | `ServiceEndpoint` / `HrCoreEndpoint`: missing URL outside Development = start-up error naming the key, deprecated keys warn once, a disagreeing old key is an error, localhost refused in a container. Applied to every S2S registration (also gateway, IT.Infra directory sync, Compliance DSAR collector). Blank secret = missing. Lifecycle gate wired by the approved key. Workflow trigger signs the caller proof. |
| P4 | `CodeGenerationUnavailableException` + `GenerateRequiredAsync`; the seven archive sites retry and dead-letter, `archive_reference_failures_total`. |
| P5 | Legal pack as a `Content` item of the shared kernel (output and publish), one loader `ServiceRegimePack`, `ready` check, start-up fingerprint line, in HR.Core, HR.Contracts, HR.Movements. |
| P6 | Prod-like stack from the shared wiring: AC-INT-37a..h 8/8 Pass, `RecruitmentToHireJourneyProdLike` Pass; lab stack smoke AC-01..04, AC-31 5/5. |
| P7 | Demo server: missing `SVC_AUTH_DIRECTORY_CLIENT_SECRET` generated on the server; realm synced from the matrix without a re-import (`keycloak-sync-clients.sh`, idempotent: second run `changes=0`); release `9d457dab36d7`; smoke 49 pass / 0 fail / 0 deferred; legal-pack fingerprint matches; AC-OPS-36 server checks 10/10. |
| P8 | HARD-001 scan in `scripts/ci/steps.json` (local CI parity); post-merge dedupe guard (fails open). |
| P9 | Docs, gap status lines; one independent review (0 high, 2 medium, 7 low), all fixed with tests; the fixed Keycloak sync and smoke verified on the server; local CI + pre-push on the release head; this report; release PR. |

## 2. Commits

After the review (P9), then the release merge `release/plan-37` (`--no-ff` from `main`):

```text
bc858c58 fix(int037): keycloak-sync admin login keeps stdin open after the password (Keycloak 24 prompt hung on an immediate EOF), bounded with three attempts
ecf66cde style(int037): nested foreach indentation (dotnet format); progress: resume stamp and review result
69ecbaa6 fix(int037): review findings: secrets off command lines, no localhost wiring outside Development, start-up secret check, usable-rule readiness, archive schema drop, sync manages retired scopes
a683af2d test(int037): server proof scenarios skip through AcceptanceGate (Blocked evidence outside the repository without VPS_SSH_HOST)
```

Before:

```text
013c1be6 docs(int037): wiring matrix, legal pack shipping, Keycloak sync and mandatory smoke documented; gap status lines; plan implemented with deviations
a3d94082 ci(int037): HARD-001 scan in local CI, post-merge run skipped only when the PR already tested the identical tree (fails open)
c4159a4f test(int037): demo server wired (service clients and secrets, mandatory wiring smoke green), legal pack fingerprint on the server, PLAN-36 checks still green
9d457dab fix(int037): keycloak-sync-clients.sh sets client secrets with kcadm -s (update -f --merge answers unknown_error on Keycloak 24)
de5737d3 fix(int037): keycloak-sync-clients.sh reads whole kcadm outputs (head under pipefail ended the run with SIGPIPE)
7f3ae42e test(int037): AC-INT-37a..h pass on the prod-like stack that takes its wiring from the production files, evidence
8a3d7f86 feat(int037): service-regimes legal pack shipped in every reading service, one loader, readiness check and start-up fingerprint
1907998b fix(int037): archive consumers retry and dead-letter when no reference can be generated, never acknowledge without an archive item
aaddd550 feat(int037): services refuse to start without their HR.Core URL or secret, one HR.Core key with deprecated fallbacks, lifecycle gate wired by the approved key, workflow trigger signs the gateway proof
5f44419a feat(int037): deployment wiring written once and checked against the matrix (compose, k8s, Keycloak script, prod-like parity), mandatory wiring smoke
3786ba06 feat(int037): service wiring matrix with calls, scopes and legal packs, rendered doc, server allow-lists aligned, unused HR.Core registrations removed
28abd186 chore(int037): baseline, code and deployment wiring inventory, server before-picture
aaffbcc1 docs(program): owner accepted the wave-1 decisions (PLAN-37..65)
035ff903 docs(ops036): PR #30 merge note and the HARD-001 follow-up
fdae391c docs(int037): PLAN-37 agent prompt
```

## 3. Decisions applied and deviations

- **D-INT-1..5** as accepted on 2026-10-31: `Content` from the shared kernel + optional `LegalPacks:ServiceRegimesPath`;
  a strategic `wiring-patch.yaml` per Deployment; deprecated HR.Core keys with one warning (a conflict is an error);
  unused enforce clients get the URL, not `hr-core.workflow`; start-up fails on a missing URL outside Development and
  on localhost in a container.
- **Deviations** (also in the plan's «ملاحظات التنفيذ والانحرافات»):
  1. `svc-auth-directory` not added to `Service.HrCoreLookup`: its only call is `/api/lookup/*` under
     `HrManagementPolicies.HrCoreLookup` (scope only). Existing allow-list entries without a call are kept and listed in
     `allowListExceptions` (removal is PLAN-42); any other unjustified entry fails the reverse check.
  2. YamlDotNet's `MergingParser` rejects an explicit key that overrides a merged anchor; `ComposeEffectiveEnvironment`
     resolves `<<` itself. The rendered `docker compose config` stays the final judge.
  3. k8s: `staging-ha` had no `SVC_*` key at all (13 added); `dr-secondary` uses the base placeholder Secret (dev-only
     `replace-me` keys added); gateway URLs moved from `hr-yarp-config` to its patch.
  4. The required-URL rule also covers the gateway, IT.Infra directory sync and the Compliance DSAR collector (DoD "no
     S2S client without a URL"). `PayrollGradeLookup` / `CandidateDocumentUpload` keep their explicit "unavailable"
     implementations (no client is registered without a URL).
  5. Found in passing and fixed: HR.Core's hire hand-off redirected browsers to `http://localhost:5000` in Production;
     IT.Infra's development Auth.Directory port pointed at File.Storage.
  6. `docs/syr/*.json` pinned to LF in `.gitattributes`: on Windows CRLF made the local fingerprint differ from the
     committed blob the server builds from. The committed blob is the reference.
  7. AC-INT-37g negative is 401 (no `api-hr-core` audience without a scope), not 403; 37c issues the card through
     `IssuedCards/Renew`; 37d publishes `ILeaveApproved` as HR.Contracts does (the UI approval is broken, §11).
  8. P-4/P-5 on the server: the first build was stopped after the sync script needed a fix; the first deploy failed in
     the Keycloak sync and was rolled back; after the fix the images built for `de5737d30659` were tagged for
     `9d457dab36d7` (only `deploy/vps/keycloak-sync-clients.sh` differs), so the server built once.
  9. `AcOps36VpsBootTests` re-run without 36j (a sequence check that expects the state right after a rollback); the
     rollback itself was exercised for real in P7.
  10. `scripts/acceptance-up.ps1` without `-NoBuild` cannot run under PS 5.1 here (missing local base images); images
      were built with `docker compose build`, one service at a time (BuildKit GC, §11).

## 4. Inventory differences from the plan's §1 table

See [plan37-baseline.md](plan37-baseline.md) §0.3. In short: Auth.Directory's lookup is not under the S2S allow-list
policy (deviation 1); the HR.Contracts lifecycle gate has no service token (authentication is PLAN-42; the call is in the
matrix with `authenticated: false`); the gateway has no service identity (user tokens only) but two localhost fallbacks;
IT.Infra's directory sync silently "succeeded" without a URL; unused enforce registrations in eight services keep the
URL only; HR.Contracts and HR.Core appsettings requested fewer scopes than their code needs.

## 5. Matrix summary

15 services (14 with wiring; File.Storage is called only), 13 service clients (`svc-*`), 8 scopes, 15 endpoints with
their server policies, 13 registrations (+ 5 ignored with a reason), 33 calls (one unauthenticated: the legal-hold gate,
PLAN-42), 2 allow-list exception groups, 1 legal pack read by 3 services. Rendered:
[SERVICE_WIRING_MATRIX.md](../docs/SERVICE_WIRING_MATRIX.md).

## 6. Results

- Build `-warnaserror`: 0 warnings, 0 errors at every phase.
- Unit: 4439 at P8, 4456 after the review fixes (§6a). Integration: 369 passed / 60 skipped / 0 failed (P3);
  `Int037ArchiveRetryIntegrationTests`, `Int037PublishOutputTests` (2) pass.
- `scripts/int037-wiring-check.ps1`: production, single-host, prod-like, parity, k8s production-ha / staging-ha /
  dr-secondary PASS; a copy without one URL fails naming the service and the key.
- Acceptance (prod-like, `ACCEPTANCE_REQUIRE_ALL=true`, 2026-11-01T00:54Z): AC-INT-37a..h 8/8 Pass (146 s),
  `RecruitmentToHireJourneyProdLike` Pass (19 s). Evidence: `acceptance-evidence/AC-INT-37/`, `AC-HCM-33/prodlike`.
- Lab stack with the shared wiring: AC-01, 02, 03, 04, 31: 5/5.
- Server: `AcInt37VpsServerTests` 3/3, `AcOps36VpsBootTests` 36a-i, 36k: 10/10. Evidence:
  `acceptance-evidence/AC-INT-37/vps/`, `acceptance-evidence/AC-OPS-36/vps/`.

### 6a. Independent review and fixes

One read-only reviewer over `19f7172b..a683af2d` (economy mode: one review). No high finding, so no refuter; every
finding was fixed with a test in `69ecbaa6`:

| # | Severity | Finding | Fix |
|---|---|---|---|
| 1 | medium | The recruitment view still fell back to `http://localhost:5000` (the hire-wizard link on the server); the localhost scan read only `*.cs` | relative link as in the controller; the scan includes `*.cshtml` |
| 2 | medium | Secrets on process command lines visible to the host (`kcadm --password`, `-s secret=`, smoke's `client_secret=`, bearer token, `docker exec -e PROOF=`) | admin password on kcadm's stdin, secret through a private temp file (`update -f`, verified on Keycloak 24), smoke probes through stdin / temp files |
| 3 | low-medium | Readiness counted array lengths, not usable rules | the counts are what each reader loads (`Parse`); a pack of unusable entries fails readiness |
| 4 | low | Base `appsettings.json` set localhost service URLs (Admin.Logistics, Payroll, IT.Infra); two HR.Core clients took any URL | moved to Development / the code default; `ServiceEndpoint.Optional`; test over every non-Development appsettings |
| 5 | low | The secret was checked only on the first token request | `ServiceIdentityStartupCheck`: a deployed service with S2S clients stops at start-up without id or secret |
| 6 | low | The archive's medical consumers acknowledged a newer schema without an item | `EventSchemaVersion.Require` → `UnsupportedEventSchemaException` (dead-lettered, no retries) |
| 7 | low | The wiring checker could print a literal k8s secret; shell variables could leak into rendered compose | only the source kind is printed; the compose child process runs without the env-file keys and secret-looking variables |
| 8 | low | The fast compose reader treated value-less entries as `""` and ignored service-level merges | value-less = unset; service-level `<<` resolved; test |
| 9 | low | The Keycloak sync stopped managing a scope that left the catalog; default scopes unchecked | every non-built-in optional scope not in the matrix and every non-built-in default scope is unlinked |

Checked clean by the reviewer: allow-lists vs calls, scopes vs code, no wiring in the acceptance files, every S2S
registration has a required URL, the seven archive sites, `HrCoreEndpoint` precedence, the CI dedupe cases (squash,
rebase, fast-forward, several PRs, re-runs, expired artifacts: each runs everything or compares the exact tree),
`set -e`/`pipefail` behaviour of the server scripts, no secret in smoke output or evidence.

On the server (no redeploy; it still runs `9d457dab36d7`): the fixed `keycloak-sync-clients.sh --check` →
`changes=0`; the fixed `smoke.sh` → 46 pass / 0 fail (`--no-login`), all 13 tokens and the S2S call Pass. The next
deploy ships both. Unit tests after the fixes: **4456 / 4456**.

## 7. Server before and after

| Check (smoke `--require-integration-wiring`) | Before (00926107a37b) | After (9d457dab36d7) |
|---|---|---|
| hr-core Payroll/FileStorage URLs and scopes | Deferred(PLAN-37) | Pass (`wiring-env hr-core-web`, `wiring-token svc-hr-core` with 3 scopes) |
| Integration__HrCoreBaseUrl for Contracts, IDCard, Archive, IT | Deferred(PLAN-37) | Pass (`wiring-env` for all 14 wired services) |
| S2S token issue per service client | Deferred(PLAN-37) | Pass (`wiring-token` for 13 clients, every requested scope granted) |
| candidate document upload to File.Storage (S2S) | Deferred(PLAN-37) | Pass (signed call, 404 for an unknown file = authorized) |
| per-service SVC_* secrets for every client | Deferred(PLAN-37) | Pass (13 keys in .env and in each container, lengths only) |
| summary | 16 pass, 0 fail, 5 deferred | 49 pass, 0 fail, 1 warn, 0 deferred |

New on the server: the legal-pack fingerprint check in the three images (sha256 `afc1cee0…` = committed file).

## 8. Server changes and their undo

| Change | Undo |
|---|---|
| `SVC_AUTH_DIRECTORY_CLIENT_SECRET` appended to `/opt/hrm/shared/.env` (backup `.env.bak-plan37-<ts>`, 600) | restore the backup or delete the line, redeploy |
| Keycloak realm: 6 client scopes with audience mappers, clients svc-hr-movements, svc-compliance, svc-hr-health, svc-dev-analytics created, svc-auth-directory enabled, optional scopes linked, 5 secrets set | Keycloak console (OPS_VPS_SINGLE_HOST §7); additive, the old release ignores them |
| Releases `de5737d30659` (failed, rolled back) and `9d457dab36d7` (current); `deployments.log` lines | `deploy.ps1 -Rollback` (returns to 00926107a37b) |
| Registry tags `:de5737d30659`, `:9d457dab36d7` | `docker image rm` + registry tag delete |
| Leftovers of the stopped first build (`7f3ae42e4b6f`: 8 local images, archive, build dir) | already removed |

Local: the PLAN-36 rehearsal stack `hrm-vps-local` was stopped (not removed) to free the Docker VM; restart with
`docker start $(docker ps -aq --filter label=com.docker.compose.project=hrm-vps-local)`. The `hrm-acceptance` stack
was stopped (not removed) in P9: with it running, local CI's Testcontainers tests timed out connecting to PostgreSQL;
restart it the same way with `project=hrm-acceptance`.

## 9. CI minutes

- No GitHub Actions minutes used before the release PR (one push, in P9).
- Post-merge guard: on a `push` to main, a light `dedupe` job skips CI build-and-test (+ E2E and Acceptance through
  `needs`) and CodeQL only when the merged PR's latest run of `ci.yml` for its head succeeded and its uploaded
  `tested-tree` equals `HEAD^{tree}`. Everything else runs (no PR, another tree, a failed or missing run, any error,
  a failed dedupe job). Gitleaks and the cheap path-filtered workflows always run.
- Expected saving per merge: CI 53 + E2E 21 + CodeQL 23 ≈ **97 billable minutes** (job durations of push run
  41247555134); the dedupe job costs ~1 minute per run.
- PR [#32](https://github.com/omardefdev/HRManagement/pull/32): three CI rounds. Two CodeQL failures (cs/exposure-of-sensitive-information on the medical archive logs; the reference factory's medical type argument read as private data) were fixed in `443c6f04` and `a2130040`; the third round was green (CI 26m48s, E2E 14m24s, CodeQL 12m19s). Merged 2026-11-01T10:11:41Z (`d1642d41`).
- **Post-merge run 41296399636:** `dedupe` printed `skip=true` (PR #32 tested the same tree); CI, E2E, CodeQL and Acceptance were skipped; 57 s, about 3 billable minutes, so **about 97 minutes saved** on this merge.
- PLAN-37 used about 167 billable minutes in total (PR ci 17 + 63 + 58, hardening-check 8, iac-plan 15, post-merge 6).

## 10. Deferred items

- PLAN-42: a secret per service in k8s, rejecting placeholder secrets, an unforgeable gateway proof, an authenticated and
  fail-closed legal-hold gate, removing the allow-list exceptions, ignoring `TenantId` in code requests.
- PLAN-39: the production realm generated from the matrix (and its check in `ProductionWiringMatrixTests`).
- PLAN-38: first-boot secrets and broker users; PLAN-48: number prefixes; PLAN-49: removing the unused enforce
  registrations; PLAN-50: approver access to movements; PLAN-61: the separation catalogue; PLAN-45/46: the custody loop.
- Wave 2: removing the deprecated HR.Core keys; reading the legal pack through an API (POL-F).

## 11. Defects for later plans

1. **HR.Contracts leave approval always fails on PostgreSQL** (`ApproveLeaveRequestHandler`: `ExecuteUpdate` claims the
   row, then `SaveChanges` updates the tracked entity against the old `xmin` → `DbUpdateConcurrencyException`, HTTP 500).
   No test approves a leave against PostgreSQL. Check `AdvanceRequestHandlers` for the same pattern.
2. **IDCard `IssuedCards/Create` can never succeed**: it binds the `IssuedCard` entity, whose non-nullable
   `CardDesign` navigation is implicitly required; use a view model.
3. Legacy asset form: posting `Model` clashes with the action parameter `model` (MVC1004, already recorded).
4. Local image builds lose packages when the BuildKit cache exceeds Docker Desktop's GC limit (NETSDK1064); prune or
   build one service at a time.
5. `acceptance-up.ps1` (build path) and `acceptance-verify.ps1` still need PowerShell 7 or the local base images.
6. `smoke.sh`'s outside port probe stays inconclusive from the owner's network (VPN), as in PLAN-36.

## 12. Timing

- **Wall-clock** from `Started (UTC)` 2026-10-31T22:24:15Z to the release PR hand-over ≈ 4 h 30 min (one session, one
  context compaction in P9, recorded as `Resumed (UTC)`). **Owner-wait time: 0** up to the hand-over (no owner action
  before P9); the P9 push, PR and merge wait is recorded in `plan37-progress.md` as the `Waiting for owner` / `Owner action
  done` pair.
- Per phase (from the `Finished (UTC)` stamps): P0 8 min, P1 11 min, P2 24 min, P3 18 min, P4 4 min, P5 9 min,
  P6 1 h 28 min, P7 56 min, P8 2 min, P9 (docs, review, fixes, local CI) the rest.
- **Acceptance:** AC-INT-37a..h 146 s; `RecruitmentToHireJourneyProdLike` 19 s; prod-like stack start 199 s (220 s the
  first time); `AcInt37VpsServerTests` and `AcOps36VpsBootTests` 286 s on the server.
- **Builds:** local images (P6) 642 + 97 + 308 + 435 + 641 + 277 + 365 s; about 46 min of it lost to the BuildKit GC
  (§11). Server build-on-host for `de5737d30659` 287 s (upload 18 s).
- **Deploys:** `de5737d30659` 142 s (failed in the Keycloak sync), rollback 80 s; `9d457dab36d7` 209 s (sync 116 s).
- **Local CI:** about 11 min for a full `ci-local.ps1` run; the release-head result is in the PR description.
- **GitHub Actions:** 0 minutes before the release PR; the PR's own runs and the post-merge run are recorded at merge.
- `scripts/plan-timing.ps1 -From 25 -To 37` (local times, before the merge; PLAN-37's End and WallClock change at the
  merge):

```text
Plan    Commits Started          FirstCommit      End              Merged Span   WallClock
PLAN-34      17 2026-10-30 02:13 2026-10-30 02:24 2026-10-30 16:11   True 13:47  13:58
PLAN-35      49 2026-10-30 16:38 2026-10-30 17:07 2026-10-31 04:21   True 11:13  11:43
PLAN-36      26 2026-10-31 16:32 2026-10-31 16:41 2026-11-01 00:13   True 7:31   7:40
PLAN-37      15 2026-11-01 01:24 2026-11-01 01:30 2026-11-01 05:34  False 4:03   4:09
```

  (PLAN-25..33 have no `Started` stamp, so no wall-clock: see the script's full output.)

## 13. Readiness note for PLAN-38

- **Contract (P-8):** any plan that adds an S2S client, call or scope, changes a server policy for a called endpoint, or
  removes a registration updates `deploy/wiring/service-wiring.json` in the same commit; `ProductionWiringMatrixTests`
  (code → matrix, matrix → policies, matrix → compose/k8s, prod-like parity, Keycloak scripts) and
  `scripts/int037-wiring-check.ps1` enforce it, and the server's smoke and `keycloak-sync-clients.sh` read the same file.
- PLAN-38 adds secrets and broker users: new `.env` keys go into `deploy/vps/.env.vps.example` (the smoke and
  `remote-deploy.sh check-env` read it); service secrets keep the `SVC_<NAME>_CLIENT_SECRET` names of the matrix.
- The demo server runs `9d457dab36d7` with every cross-service flow wired; demo mode (administrator DB/broker accounts)
  is unchanged until PLAN-38.
