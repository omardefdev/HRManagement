## PLAN-39: Keycloak realm as code and persistent Data Protection keys

**Realm as code.** `docker/keycloak/realm-manifest.json` (no secret) + the PLAN-37 wiring matrix → `scripts/keycloak/render-realm.ps1`
for the Production, Demo and Acceptance profiles; secrets are `${NAME}` placeholders resolved by Keycloak at import.
First boot imports it (`--import-realm` from the `keycloak_import` volume); the one-shot `keycloak-reconcile`
(`deploy/keycloak/reconcile.sh`, Keycloak image) keeps clients, scopes, secrets, redirect URIs and the security fields in
step on every deploy — never users or role mappings; a second run prints `reconcile: 0 changes`. It replaces PLAN-37's
`keycloak-sync-clients.sh`. Production/Demo: `sslRequired=external`, brute force 5 failures / 15 min,
`length(12) and notUsername and passwordHistory(3)`, no gateway password grant; Production has one bootstrap
administrator with `UPDATE_PASSWORD`.

**Keycloak on its own database role** `keycloak` (owner of `keycloak_db`, no CONNECT elsewhere), made by `hrm-bootstrap`;
a documented, switch-guarded, object-by-object ownership transfer for existing servers. The directory-sync secret is
required only when directory sync is enabled.

**Key rings never silently lost.** Start-up checks run before any migration or seed (top of `TryMigrateAsync`):
`DataProtectionStartupGuard` (persistent, writable key ring, application name), HR.Core's `EmployeePiiKeyRingCanary`
(refuses to start when the ring cannot read a stored national ID; `DataProtection:AllowKeyRingReset=true` → Critical +
counter), and the configuration guard (moved earlier). Optional certificate protection, `make-dp-cert.sh`,
`export-dp-keys.sh` (not enabled on the demo server).

**Proof.** AC-OPS-39a..h 8/8 on the prod-like stack; Testcontainers Keycloak 24 + PostgreSQL (Production import, tokens and
audiences, reconcile idempotence and rotation), PostgreSQL (keycloak role and transfer; canary under RLS); AcHcm25/30
green. Demo server moved in place after a verified backup (`/opt/hrm/backups/pre-plan39-20261102T014546Z`): 285
relations transferred, Demo security settings reconciled, canary green on 500 protected national IDs, downtime 3 min 10 s;
PLAN-36..38 server checks green.

Report: `reports/plan39-final-report.md`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
