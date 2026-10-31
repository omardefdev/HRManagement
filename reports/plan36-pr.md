## PLAN-36: production profile on a single host

The production compose chain now boots by itself, and one server runs the whole system in **demo mode** (synthetic data only) behind a Caddy TLS edge at https://hr.161-97-185-71.sslip.io/.

- **Production chain:** valid without the observability file; explicit gateway settings; caller-proof secret required; File.Storage on the Local provider; MinIO behind a profile; Data Protection volumes; restart policies and a one-shot volume-ownership step.
- **`ProductionConfigurationGuard`:** refuses placeholder values in Production and names the key in Arabic and English, never the value. The license middleware no longer checks health endpoints and parses dates culture-independently.
- **Gateway rate limiter:** partitions signed-in users by subject and anonymous callers by IP, exempts static assets, and now runs after authentication.
- **Edge:** Caddy publishes only 443, gets Let's Encrypt certificates via TLS-ALPN, answers ops paths 404 and Keycloak admin 403. Keycloak runs behind it with `KC_PROXY_HEADERS`. Memory budget, demo mode, compose lint.
- **Deploy tooling:** registry images by `<sha12>`, a deploy bundle without secrets, deploy/rollback over ssh, idempotent host setup with gated SSH hardening, a VpsDemo realm whose secrets stay placeholders, and a smoke test.
- **Acceptance:** AC-OPS-36a..k pass 11/11 on the local single-host chain and 11/11 on the real server. The server also survives a reboot and passes redeploy and rollback.
- **Also in this PR:** the five local `main` commits from after PLAN-35 (defect 9, Playwright traces; defect 15, gateway 429 at once).

Report: [reports/plan36-final-report.md](plan36-final-report.md) · evidence: `acceptance-evidence/AC-OPS-36/` (local) and `acceptance-evidence/AC-OPS-36/vps/` (server) · runbook: [docs/OPS_VPS_SINGLE_HOST.md](../docs/OPS_VPS_SINGLE_HOST.md)

**Deviations:**
- Contabo with 8 vCPU, 23.5 GiB and Ubuntu 26.04, instead of the plan's Hostinger server.
- Images are built **on the server** and served from a loopback registry. Pushes to GHCR failed on the owner's network, and the owner's upload is about 0.16 MB/s.
- sslip.io names instead of a domain.
- MinIO credentials and image use `${VAR:-}` (guarded by the lint and the configuration guard) instead of `:?`.
- Memory limits add up to 13.5 GiB, not 13.1, because there are ten small services rather than nine.
- External port checks use a control port, because the owner's path answers every TCP port; closed ports are proven on the server side.

**Local CI on the release head:** see the result added below before the PR was opened.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
