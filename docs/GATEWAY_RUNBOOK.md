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
  `/ops/metrics-summary` carries the same policy; its recent error paths are for `platform-admin` only.
- **Rows written before PLAN-51 have no agency:** the queue's copy written to the database dropped `TenantId` (and `ScopeOrgUnitId`,
  `Reason`, `ResourceType`, `ResourceId`) until PLAN-51 fixed `GatewayAuditQueue.CloneEntry`. Those rows stay visible to `platform-admin`
  only; they are not backfilled (on the demo server: 10095 rows on 2026-11-12).
- **What is audited:** the requests the gateway forwards (`/ui/...`, `/api/...`) and its own refusals of them; its own pages
  (`/Home/...`, `/ops/...`) write no audit row.
- **A refused page:** the gateway's own pages refuse the way its cookie scheme does, with a 302 to `/Account/AccessDenied`, a page the
  gateway does not have (404). The refusal holds; the page a refused user lands on is a known defect (PLAN-51 report §10).

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

### Security headers: one policy per response (PLAN-67, D-67-1)

- `UseHrSecurityHeaders` writes every security header in `Response.OnStarting`, each **only if the response does not already carry
  it**; a Content-Security-Policy in either mode (enforced or Report-Only) counts. The gateway's middleware runs before
  `MapReverseProxy`, so a forwarded page keeps its service's policy and nonce, and only a gateway page, or a forwarded answer without
  headers, gets the gateway's. Before PLAN-67 the gateway wrote its own policy first and YARP added the service's: two policies, whose
  nonces matched neither page, blocked every inline style and script that was not from the origin.
- One exception keeps a page protected: if the gateway enforces and a forwarded answer carries only a Report-Only policy (a service
  left in `ReportOnly`), the gateway sends that same policy as enforced, nonce and all.
- `Security:CspMode` is `Enforce` in every Production file. Check a live answer with
  `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ux067-validate.ps1 -Headers <url>[,<url>…]`: exactly one policy, no
  Report-Only, nothing unsafe.

### The public portal at `/careers` (PLAN-67, D-67-5)

- The route `/careers/{**catch-all}` goes to HR.Core with `/careers` as its path base, anonymous. HR.Core's `[PublicPortalOnly]`
  constraint (on `PublicCareersController` and `CandidatePortalController`) answers only through `/careers` (or with no path base, the
  service reached directly), so `/ui/hr-core/` opens the staff dashboard and `/ui/hr-core/track` or `/ui/hr-core/my` answer 404.
- Every public link keeps its address: `/careers`, a posting, `/careers/track`, `/careers/my`, competition results and their PDF.
- `HEAD /careers` answers 302 to sign-in while `GET` is public (seen in P0, unchanged here).

### The user-name registry (PLAN-67, D-67-3)

- `GatewayUserNameRegistryMiddleware` (after authorization) queues each signed-in **interactive** user's `sub`, display name and
  agency; `GatewayUserNameWriter` (a background service) writes them to `gateway_user_names` (database `gateway_audit_db`, migration
  `UX067_UserNameRegistry`), at most once an hour per user (memory cache), and a changed name at once. A request never waits for the
  database: if it is unreachable, the writer logs a warning and tries again a minute later. A service account or a user without a
  name is never recorded. The agency column is `AgencyId`, not `TenantId`: the bootstrap's TEN-002C hardening forces tenant row
  security on every `TenantId` column, and the gateway sets no tenant, so the production role would read and write nothing; the
  lookup filters by agency in its own query.
- `GET api/user-names/token` hands out the gateway's antiforgery token (and its cookie); `POST api/user-names/by-ids` (at most 50 ids,
  with the token in `RequestVerificationToken`) answers `{ items: [{ id, label }] }`. Both need `Gateway.UserNames` (interactive staff,
  PLAN-50's definition): an employee-only user or a service is refused (403). The answer holds names of the caller's agency only
  (every agency for `platform-admin`), the name only, and leaves out an id it does not know.
- `display-names.js` fills each `[data-hr-user-id]` (`_UserDisplayName`) from it; an automated actor shows «النظام», and an id the
  registry does not know stays «مستخدم غير معروف» with the id in a tooltip. A name appears after its owner's first sign-in following
  the deploy.
- **The gateway's own API answers its status as it is.** `UseStatusCodePagesWithReExecute` stays for pages and proxied routes, but
  a middleware after routing turns it off for the gateway's `[ApiController]` endpoints: before PLAN-67 an empty 403 there was re-run
  as `POST /Home/ErrorStatus/403`, a GET-only action, and answered 405.

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
