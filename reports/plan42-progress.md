Started (UTC): 2026-11-04T22:47:33.7442492Z

# PLAN-42 progress

## P0

Preconditions (2026-11-04):
1. PLAN-41 is not merged: PR 36 open, run 41918380549 on 3021ea9a, Dedupe, Gitleaks, CodeQL, CI, E2E green, Acceptance in progress. Its P7 stands at step 3 of 8 and `reports/plan41-final-report.md` does not exist. This session finishes it: evidence commit, run 2, merge, post-merge items, final report.
2. Not checked yet: main moves with that merge.
3. Tree clean except the allowed items.
4. No other plan session runs (the PLAN-41 session ended 2026-11-04T21:41Z).
5. ssh answers `hrm`, host key matches the plan.
6. gh: omardefdev, repo and workflow scopes.

Decision (deviation from the order of P0, recorded for the final report): the wait for CI is about 3 hours of wall-clock in all, so the baseline and P1 work started on `feature/plan-42-s2s-trust-boundary` cut from the PR head 3021ea9a, not from the updated main. Nothing is pushed from this branch until PR 36 is merged and the branch is rebased onto main, so the result equals the prompt's. PR 36 is still merged only on green checks.

Waiting for CI: this app does not allow polling (`gh run watch` in the background is not permitted after a PR is open), so the state is read from the app's PR status.

Times: build 65 s; baseline unit 77 s; baseline integration 37 s, then 24 s of tests on Testcontainers; secret inventory (subagent) 12 min 47 s.

Corrections: a first version of the baseline explained the 19 skipped integration tests wrongly; the true gate is RUN_TESTCONTAINERS, and the rerun gave 27 passed.

Notes:
- Bash heredocs containing Arabic fail in this session's shell: Arabic edits go through the Edit or Write tools.
- The inventory subagent's grep printed the lab-only values of the tracked `deploy/vps/.env.vps-local` into its own output. None reached a report.

Next step: commit P0, commit P1 after its tests, then the PLAN-41 evidence commit as soon as run 41918380549 is green.

Finished (UTC): 2026-11-04T23:05:00Z (P0, committed before P1)

## P1

The client is rewritten to rules 1-6; PolicyModeResolver, PolicyEngineConfigurationValidator (AddHrStartupCheck), PolicyClientMetrics; the four HR.Core mode copies are gone; default request mode Off; IDCard snapshot store and consumer removed; AC-MSG-40c proves delivery at the publisher (outbox sequence moved, no row left for the MessageId).
Beyond the plan: AssetClassifier read Decision and ignored Allowed, so an outage would have marked a privileged asset as not privileged. It now returns the refusal and the three callers stop. AllowWithAlert in Production fails the boot.
Tests: S2s042PolicyEnforceClientTests in a 187-test run, 0 failed.
Commit eea5c98a. Finished (UTC): 2026-11-04T23:07:11Z

## P2

PolicyEnforceInternalController, PolicyDomainOwners, PolicyWorkflowTriggerAllowlist, EnforceForServiceAsync, S2S042_PolicyLedgerCaller, the reuse rule, the gateway rule. evaluate reads the ambient tenant.
P-6: the svc-* secret of the acceptance realm is generated into .acceptance/credentials.json (serviceClientSecret), exported as ACCEPTANCE_SVC_CLIENT_SECRET; no literal is left in the acceptance files. One secret for all clients, because docker-compose.acceptance.yml sets one ServiceIdentity__ClientSecret; AcceptanceHarness also reads ACCEPTANCE_SVC_<CLIENT>_SECRET first.
The lab stack sets PolicyEngine__Assets.Custody=Enforce on hr-core-web only: the server derives the mode now, AC-10 and AC-11 need it, and admin-logistics-web stays Off and never calls.
AC-09: its body can no longer ask for Shadow, so it writes Blocked with the reason (AC-S2S-42b proves Shadow; PLAN-65 rewrites it). AC-10 moved to the service identity.
CodeQL: the two new internal POST controllers carry [ValidateAntiForgeryToken] on the class and [IgnoreAntiforgeryToken] on the bearer-only action; the baseline was not touched.
Tests: unit 95 in the wiring and realm filter, integration S2s042 7 of 7 on Testcontainers. has-pending-model-changes: none.
Commit 4b459f09. Finished (UTC): 2026-11-04T23:20:29Z

## P3

GatewayCallerProof v2, GatewayProofKeys, GatewayProofReplayCache, GatewayProofSigningHandler, middleware. Services stop signing (5 sites). The gateway signs in 3 places: the transform, HomeController, and hr-core-journey, which never signed (a gateway call that production refused before this plan).
IsServiceAccount: azp decides when present.
scripts/s2s042-new-gateway-proof-keys.ps1 (openssl, then tools/S2sKeyGen; both run here), both init-secrets, compose, smoke (a service token, no proof), remote-deploy, deploy -Local (the pair goes to the working copy of the env file, never the tracked one).
Tests: 327 in the guard, env-parity and S2s042 filter. lint-compose and int037-wiring-check PASS.
Commit 9570e368. Finished (UTC): 2026-11-04T23:40:00Z

## P4

Per-service ExternalSecrets for production-ha and staging-ha (15 + hr-migrate-secrets), per-service dev placeholders in base, removed by label. Rendered with kustomize v5.4.3 in Docker (kubectl is not installed here): production-ha gives 16 ExternalSecrets, 0 Secret objects, 0 mentions of hr-app-secrets.
Names follow rule 1 of the plan (<deployment>-secrets, so auth-directory-web-secrets); rule 2 writes auth-directory-secrets. Recorded as a deviation.
Guard: a placeholder in any key fails outside Development, Testing and Acceptance, Staging included; markers add replace-me and __X__. Module with a private key refuses to start.
Scope dsar.subject.erase: wiring and compose and k8s; realm-manifest.json needs no entry, render-realm takes scopes from the wiring. Class-level DsarSubjectRead stays, so anonymize needs both scopes; svc-compliance asks for both.
Tests: 254 in the S2s042, wiring and realm filter.
Commit 4a423a9b. Finished (UTC): 2026-11-04T23:47:10Z

Next step: P5 (codes, legal-hold gate, internal routes). PR 36: run 41934216395 on 2d2a349b is in progress; when green, make the evidence commit in the clone D:/hrm-plan41-finish.

## P5

Owners of official numbers (`CodeKindOwners`) and `GenerateForServiceAsync` on `api/internal/codes/generate`; `IssuedByClientId` with migration `S2S042_CodeIssuanceCaller`. Policies `HrCoreInternalS2S`, `HrCoreLifecycleS2S`, `CodeGenerate`; `HrCoreLookupS2S` removed; DirectorySync for ItInfra only. `HttpEmployeeLifecycleGate` authenticates, fails closed, and is checked at start by `EmployeeLifecycleGateStartupCheck`. CodeQL baseline entries 034 and 052 are Fixed. Tests: S2s042CodeGenerationAuthTests, S2s042LifecycleGateTests, S2s042InternalRoutesAreServiceOnlyTests, S2s042CodeIssuanceTenantPostgresTests.

Commit 88578d6e. Finished (UTC): 2026-11-04T23:57:54Z

## P6

Resumed (UTC): 2026-11-05T00:51:24Z

Live findings on the prod-like stack, each fixed in the working tree:

- The image entrypoint was `sh -c`. Dash drops environment variables with a dot in the name, so `PolicyEngine__Assets.Custody` never reached the process and every such mode ran Off. The entrypoint is now bash, locked by a unit test. This also makes the prod-like `PolicyEngine__Core.Recruitment: Shadow` real for the first time, so AcHcm25 is rerun.
- `openssl pkey -outform DER` writes SEC1, which the loader refuses (gateway 502). Both generators use `openssl pkcs8 -topk8 -nocrypt`, and a test loads what the script makes.
- `sync-keycloak-dev-realm.ps1` reset client secrets to the lab literal and printed one. It now takes the generated secret and prints nothing.
- The seed patch needs a tenant scope (`TenantIdFlow`), or hr-core crashes at start.
- Acceptance services have no container health check, so `compose up --wait` returns at once. Readiness means `/health/ready`.
- `docker builder prune` freed 173.8 GB during this phase.

Defect for later, not fixed here: `TryAnonymize` builds the anonymized national id from the first 8 characters of the id. Seeded ids share those, so a second anonymization in one requisition breaks `IX_CitizenApplications_TenantId_NationalId_RequisitionId` (23505). With random ids the clash needs a 32-bit collision. AC-S2S-42f uses a copy with a random id.

More live findings (P6):

- `ForwardActingUserHandler` was never registered in DI, so `AddServiceAuthenticatedClient(forwardActingUser: true)` threw on the first call. Every policy enforce call failed, and fail-closed turned that into a refusal. `FileReferenceClient` uses the same flag, so its calls had the same fault before this plan. The handler is registered now, and a unit test builds the real client.
- The gateway forwarded antiforgery cookies only, so TempData flash messages never reached the service. A refused user saw the page reload with no reason. `AcHcm26` had noted this and asserted on the database instead. The gateway now forwards `.AspNetCore.Mvc.CookieTempDataProvider` by exact name; the plan's AC-42a and AC-42h need the message on the page. Recorded as a deviation.

PLAN-41 ownership: the PLAN-41 session was still alive and took PR 36 at bb3e1ae2 after CI went red (run 41948564758). It owns the merge, the post-merge items, `reports/plan41-progress.md` and `reports/plan41-final-report.md` on `release/plan-41`. My uncommitted copies of those two files are not committed from this branch. bb3e1ae2's evidence was withdrawn at bc0b5fc8. Precondition 4 of the prompt applies: no server work until that session reports the merge.

P6 results on the final code, by the job script on a fresh stack:

- Prod-like with the s2s42 overlay: RecruitmentToHireJourneyProdLikeE2ETests 1, AcHcm25 8, AcInt37 8, AcMsg40 8, AcS2s42 9 (42a..i), AcOps38 8. All passed; guard PASS, 6 classes in 6 trx files. Log `artifacts/plan42/p6-job-prodlike3.log`.
- Lab: Ac10, Ac11, Ac13 passed; Ac09 skipped as Blocked by design. Guard PASS. Log `artifacts/plan42/p6-job-lab.log`.
- Unit 4850 passed, 0 failed. Build with -warnaserror: 0 warnings. `s2s042-validate.ps1` PASS.
- Two fixes the job forced: `acceptance-job.ps1` treated every stage as having an overlay (`@($null)` has one element), and `int037-wiring-check.ps1` read only the first line of a value that `kubectl kustomize` folds. Here kubectl comes with Docker Desktop, so the k8s rendered check does run locally.
- Time to all healthy: 810 s with the image build, 247 s without. Memory: prod-like 19 containers 6784 MiB against 15.48 GiB for Docker, so two stacks do not fit; plans take turns. Measured before the bash entrypoint change, which does not affect memory.
- Integration with Testcontainers: 492 passed, 4 skipped, 3 failed. The 3 are Qa003 MinIO tests that need HRM_MINIO_IMAGE (D-QA-7), an environment setting, not this plan.

Finished (UTC): 2026-11-05T03:22:14Z (P6)
