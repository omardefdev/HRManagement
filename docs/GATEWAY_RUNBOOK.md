# Gateway Runbook

## Purpose

Operational checklist for `HRManagement.Gateway` as the single system entry point.

## Start Commands

- Core stack: `docker compose up -d --build`
- Gateway only rebuild: `docker compose up -d --build hr-gateway`
- Gateway + hr-core quick start: `docker compose up -d --build hr-core-web hr-gateway`

## Required Keycloak Clients

- `hr-gateway-client`:
  - Redirect URI: `http://localhost:5000/signin-oidc`
  - Post logout URI: `http://localhost:5000/signout-callback-oidc`
  - Confidential + client secret in `.env` as `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET`

## Health and Operations

- Gateway health: `http://localhost:5000/health`
- Upstream summary API: `http://localhost:5000/Home/UpstreamHealth`
- Operations page: `http://localhost:5000/Home/Operations`
- Metrics summary (protected): `http://localhost:5000/ops/metrics-summary`

## Audit

- Every `/ui/*` and `/api/*` request is audited with:
  - user identity (`sub`, username)
  - path/method/status/decision
  - latency and correlation id
- Audit is written to:
  - structured logs
  - PostgreSQL table: `gateway_audit_entries`
- Connection string key: `ConnectionStrings:GatewayAuditDb`
- Sensitive query values such as `token`, `client_secret`, `password` are redacted before persistence.

## Incident Triage

1. Check gateway container logs.
2. Check upstream module health in operations page.
3. Verify Keycloak availability and issuer (`/realms/hrmanagement/.well-known/openid-configuration`).
4. Verify role mapping for blocked paths (403).
5. Check correlation id in logs to trace cross-service request path.

## Security Baseline

- Keep client secrets in environment variables or secret manager only.
- Enable HTTPS in production and trusted reverse-proxy headers.
- Keep `Cors:AllowedOrigins` explicit in production configuration.
- Keep rate limiting enabled at gateway edge.
- Keep HSTS and security headers enabled in production.

## Rollout Strategy

1. Pilot users with `hr-core` enabled only (`Gateway:EnabledModules`).
2. Gradually enable additional modules by slug.
3. Monitor 401/403/502/503 and latency before widening rollout.
