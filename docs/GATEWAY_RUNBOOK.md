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
- Operations page: `http://localhost:5000/Home/Operations` (PLAN-51: `Gateway.Audit`, see Audit below)
- Metrics summary (protected): `http://localhost:5000/ops/metrics-summary`

## Self-service (`/ui/ess`, PLAN-51)

- The route `ui-ess` sends `/ui/ess/{**catch-all}` to HR.Core. The gateway matches self-service by the **first segment** after
  `/ui/{segment}/` or `/api/{segment}/`, exactly (case-insensitive, as MVC routes), never by a substring (`GatewayEssRouting`).
- `Gateway:Ess:AllowedControllers` (D-51-1): `ess` and `hr-core` = `EssPortal`, `EssTalent`; `payroll` = `Ess`. A named controller gets
  `Module.Ess`. Adding HR.Training's `EssLearning` (wave 2, IAM-H) is one line: `"hr-training": ["EssLearning"]`.
- On the `ess` segment the decision is final: a static file (`Gateway:Ess:StaticPrefixes`, GET or HEAD, no `..`) gets
  `Gateway.Authenticated`; **anything else answers 404 before forwarding**, recorded in the audit as `Denied:EssNotExposed`. Nothing under
  `/ui/ess` reaches the approval table of PLAN-50 or `Gateway.ApprovalReach`.
- On `hr-core` and `payroll` a path that names no self-service controller follows the usual rules (`/ui/hr-core/Shifts/Create/Ess` gets
  `HrCore.Write`).
- HR.Core's static files (`/ui/hr-core/{css,js,lib,fonts,images,_content}/`, the scoped bundle) get `Gateway.Authenticated`, so the
  self-service and marking pages keep their styles; the directory (`/api/lookup`) is `HrCore.Lookup`: interactive staff (PLAN-50's
  definition, `Authorization:NonStaffRoles`) or a service with `hr-core.lookup`.

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
- **Who reads it (PLAN-51, D-IAM-7):** `Home/Operations`, `Home/Audit` and `/ops/audit-search` carry `Gateway.Audit`, roles from
  `Gateway:Audit:Roles` (default `platform-admin`, `auditor`, `compliance`; not `hr-admin`). The menu links show only to its holders.
- **Whose rows:** `GatewayAuditQueryService` filters by the caller's `tenant_id` (`GatewayAuditScope`); `platform-admin` sees every agency
  and the rows with no agency (requests without a token); a caller without a valid `tenant_id` sees nothing. The filter is in the query
  service, never in a view (proved on SQLite and PostgreSQL: `Iam051GatewayAuditScopeTests`, `Iam051GatewayAuditPostgreSqlTests`).

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

## Rate Limiting

- One fixed one-minute window per client IP: `RateLimiting:PermitLimitPerMinute` (default 120, clamped to 30-2000;
  the acceptance and CI E2E stacks use 2000). Code: `Services/GatewayRateLimiting.cs`.
- The client IP is `RemoteIpAddress`. With `UseForwardedHeaders` on, it comes from `X-Forwarded-For` only when the peer
  is in `ForwardedHeaders:KnownProxies` / `KnownNetworks` (default: loopback and private ranges). If clients on a private
  range can reach the gateway directly, list the edge proxy explicitly, or they can choose their own window.
- Over the limit the gateway answers `429` at once (no queue) with `Retry-After: 60` and a short Arabic text, and logs
  one warning: `Gateway rate limit: /path answered 429 by the global limiter (ip partition, 120 per minute), retry after
  60 s`. The address and the user are not logged.
- Many 429s from one office usually mean many users behind one NAT address (PLAN-36 §4 splits signed-in users by user).

## Rollout Strategy

1. Pilot users with `hr-core` enabled only (`Gateway:EnabledModules`).
2. Gradually enable additional modules by slug.
3. Monitor 401/403/429/502/503 and latency before widening rollout.
