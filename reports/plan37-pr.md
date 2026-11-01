## PLAN-37: production wiring and the legal pack

Every service now gets, in the real production settings, the wiring its code reads: the called services' addresses, its own service identity, the exact scopes its calls need, its secret, and the SYR-003 service-regimes legal pack. The demo server runs every cross-service flow.

- **One wiring matrix** (`deploy/wiring/service-wiring.json`, rendered in `docs/SERVICE_WIRING_MATRIX.md`): 15 services, 13 service clients, 8 scopes, 33 calls. Each call is checked against the real server policies, with positive and negative cases. Compose (production, single host, prod-like), the three k8s overlays and the Keycloak scripts are generated from the matrix or checked against it (`ProductionWiringMatrixTests`, `scripts/int037-wiring-check.ps1`).
- **Clear start-up failures:** a missing service URL outside Development stops the service and names the key. So does a localhost URL inside a container, or a missing service secret in a deployment. Old HR.Core keys work with a single warning.
- **Archive consumers never drop a message:** without a reference number they retry and then dead-letter, and a newer schema is dead-lettered too. A counter tracks the failures: `archive_reference_failures_total`.
- **Legal pack shipped in every service that reads it:** it is a `Content` item of the shared kernel, loaded by one loader. A readiness check counts the rules the readers can actually use, and a start-up line logs the pack's SHA-256.
- **Prod-like stack:** it takes its wiring from the production files, so it fails where production fails. AC-INT-37a..h pass 8/8, and the recruitment-to-hire journey passes.
- **Demo server:** realm synced from the matrix without a re-import (`keycloak-sync-clients.sh`, idempotent). The mandatory wiring smoke has 0 failures and 0 deferred items, and the legal-pack fingerprint matches the repository.
- **CI:** the HARD-001 scan now also runs in local CI. After a merge, the push run skips CI, E2E and CodeQL only when the PR already tested the identical tree; any doubt runs everything. This saves about **97 Actions minutes per merge**.
- **Independent review:** 0 high, 2 medium and 7 low findings, all fixed with tests. Secrets are no longer on process command lines, there are no localhost URLs outside Development, and the retired-scope cleanup is in the Keycloak sync.

Report: [reports/plan37-final-report.md](plan37-final-report.md) · evidence: `acceptance-evidence/AC-INT-37/` (prod-like) and `acceptance-evidence/AC-INT-37/vps/` (server) · matrix: [docs/SERVICE_WIRING_MATRIX.md](../docs/SERVICE_WIRING_MATRIX.md)

**Deviations:**
- `svc-auth-directory` was not added to the HR.Core lookup allow-list, because its only call is under a scope-only policy. Allow-list entries that have no call are kept as listed exceptions, and their removal is PLAN-42.
- The legal-hold gate is wired by the approved key but is still unauthenticated (PLAN-42).
- The demo server runs `9d457dab36d7`. The review fixes to `keycloak-sync-clients.sh` and `smoke.sh` were verified on the server from a temporary copy (`changes=0`; smoke 46 pass / 0 fail), and they ship with the next deploy.
- Defects found and left to later plans: HR.Contracts leave approval fails on PostgreSQL, and IDCard `IssuedCards/Create` can never succeed (report §11).

**Local CI on the release head (8d24700a0aa3):**
- `scripts/plan035-prepush.ps1`: gitleaks, actionlint, cilocal, forbidden, nonprod and signatures all PASS (22 commits in range).
- `scripts/ci-local.ps1 -SkipTestcontainers`: 88/88 passed, with Testcontainers and the coverage gate reported as not-run (the pre-push script allows this; both run in this PR's CI).
- Three full runs on this head failed only in Testcontainers PostgreSQL concurrency tests, with connection timeouts on Docker Desktop.
- A full run on the previous release head f4692d911d55 passed 90/90. That head had the same code; only `.gitleaks.toml` and the progress file have changed since.

**After the merge:** the `ci` push run on `main` should show the `dedupe` job with `skip=true`, and build-and-test and CodeQL as skipped.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
