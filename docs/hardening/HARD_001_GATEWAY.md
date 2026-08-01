# HARD-001 — API Gateway Hardening Baseline

**Control:** HARD-001 | **Asset:** `hr-gateway` (YARP reverse proxy) | **Owner:** Platform Security

## Scope

Single north-south entry point for HR Management APIs (`hr-gateway` / `deploy/k8s/base/hr-gateway/`).

## Baseline controls

| Control | Requirement | Implementation |
|---|---|---|
| Exposure | Only Gateway publishes host port | SEC-001 — compose `GATEWAY_HTTP_PORT`; K8s Ingress → gateway Service |
| TLS termination | At edge (Ingress / reverse proxy) | `UseForwardedHeaders: true`; `KC_PROXY: edge` for IdP |
| Authentication | JWT validation via Keycloak | `Keycloak__Authority`, audience checks per route cluster |
| Authorization | Route-level policies | YARP transforms + ASP.NET authorization middleware |
| Audit | Gateway audit DB | `gateway_audit_db` — append-only access pattern |
| Rate limiting | Per-tenant / global | Gateway middleware (SEC follow-on) |
| Headers | Security headers on responses | HSTS, X-Content-Type-Options, frame deny (UX/SEC) |
| Container hardening | Full app baseline | `no-new-privileges`, `cap_drop: ALL`, non-root, resource limits |
| Upstream isolation | Apps on internal network | `application` network internal; no direct app host ports |

## Network matrix

See `docs/NETWORK_MATRIX.md` — Gateway is the only service on `edge` + `application` + `data` for audit DB.

## Health & observability

- `/health/live`, `/health/ready` — not exposed without auth on public edge where possible.
- OpenTelemetry traces exported to collector (internal network).

## Secrets

- `KEYCLOAK_HR_GATEWAY_CLIENT_SECRET` — External Secret / env injection only.
- No client secrets in config maps or images.

## Exceptions

See [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md).

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
