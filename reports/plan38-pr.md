## PLAN-38: first boot of data, broker and secrets

A new server now builds itself from empty volumes with no manual step, and every redeploy re-applies the same state without side effects. The demo server was rebuilt that way.

- **`hrm-bootstrap`** (image `hr-migrate`: 15 self-contained EF bundles + psql) runs before every application on every deploy. Every step can run again: 16 databases, 30 roles with one password each, 264 migrations as `<svc>_migrator` then TEN-002C, the read-only `hrm_backup` role, and 14 `mq_*` broker users. No password is put on a command line. The GitHub-runner `migrate` job is removed.
- **RabbitMQ:** the `hr-rabbitmq` image has the delayed-message plugin 3.13.0 (pinned by SHA-256), the fixed node `rabbit@rabbitmq` and a volume. `mq_*` users are the default; their permissions come from a live inventory (200 exchanges, 122 queues). `configure` and `read` cover only the service's own names and the shared ones.
- **Secrets:** `init-secrets.sh` / `init-secrets.ps1` fill only the missing keys and print names, never values. The examples match every `${VAR:?}`. No placeholder remains in any `appsettings.Production.json`. The production guard covers connection strings, broker and service credentials, and both postures.
- **Seeds and pools:** a failed mandatory seed stops Production start-up and names the seed. Every connection pool is bounded: 185 of `max_connections` 250. This is checked by lint and, at deploy, for values set in `.env`.
- **PLAN-37 defects fixed:** leave and advance approval now work on PostgreSQL. ID card create and edit bind a form model.
- **Acceptance:** AC-OPS-38a..h and AC-20 pass on a least-privilege stack built from empty volumes. AC-OPS-38a..h were re-run 8/8 with the review fixes.
- **Independent review:** 0 high, 2 medium and 4 low findings. Five are fixed with tests: the approval claim matches the validated request, broker `read` is limited to the service's own names, secrets are kept off the `jq` and k8s migration command lines, and the pool sizes are checked at deploy. The sixth is recorded.

**Demo server** (`https://hr.161-97-185-71.sslip.io/`):
- A verified backup was taken first: `/opt/hrm/backups/pre-plan38-20261101T175541Z`.
- It was then rebuilt from empty volumes on tag `7bd85bc3ac9d`. `hrm-bootstrap` took 48 s, and the downtime was 5 min 53 s.
- Only `mq_*` users are connected to the broker.
- Smoke: 48 pass, 0 fail. PLAN-36 and PLAN-37 server checks pass, except 36j, which redeploys and was not run.
- The two PLAN-37 defects pass through the real UI on the server: a leave approved and cancelled (balance debited once, then restored), and an ID card issued.

Report: [reports/plan38-final-report.md](plan38-final-report.md) · evidence: `acceptance-evidence/AC-OPS-38/` (stack) and `acceptance-evidence/AC-OPS-38/vps/` (server)

**Deviations:**
- The server's `.env` stays at `/opt/hrm/shared/.env`.
- The bootstrap always migrates as `*_migrator`.
- AC-OPS-36 ran on the server, not locally.
- `max_connections` is 250.
- `pg_read_all_data` is used for `hrm_backup`.
- Keycloak's database user stays the administrator until PLAN-39.

**Deferred:**
- MinIO user provisioning (D-38-7; the ObjectStorage-CI exclusion is renewed by decision `PG-OPS38-OBJECTSTORAGE-CI`).
- DB posture `least-privilege` on the server (OPS-D).
- The guard's behaviour when the posture keys are absent (review F5).

**Next deploy:**
- The server runs `7bd85bc3ac9d`. The review fixes (narrowed broker `read`, `jq` on stdin, approval claim F1) reach it with the next deploy.

**Local CI on the release head (`f76db4e1ba04`):**
- `scripts/ci-local.ps1` (full, Linux container, clean clone): 90/90 passed in 1057 s, Testcontainers and the coverage gate included.
- `scripts/plan035-prepush.ps1`: gitleaks, actionlint, cilocal, forbidden, nonprod and signatures all PASS (18 commits in range).

🤖 Generated with [Claude Code](https://claude.com/claude-code)
