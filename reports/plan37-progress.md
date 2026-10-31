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
