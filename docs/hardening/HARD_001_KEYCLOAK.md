# HARD-001 — Keycloak Hardening Baseline

**Control:** HARD-001 | **Asset:** Keycloak (Quarkus) | **Owner:** Identity Governance

## Scope

Identity Provider for HR Management (`keycloak` service / `deploy/k8s/data/base/keycloak/`).

## Baseline controls

| Control | Requirement | Implementation |
|---|---|---|
| Mode | Production `start` (not dev) | `command: start` in compose production |
| Hostname | Strict hostname validation | `KC_HOSTNAME_STRICT`, `KC_HOSTNAME_STRICT_HTTPS` |
| Proxy | Edge proxy mode | `KC_PROXY: edge` behind Gateway / Ingress |
| Database | Dedicated schema, no superuser | `keycloak_db` + scoped DB user |
| Admin console | Restricted network | Edge network + firewall; no public admin in prod |
| Brute force | Enabled | Realm brute-force detection policy |
| TLS | HTTPS at edge | Ingress TLS; `KC_HTTP_ENABLED` only for internal mesh where documented |
| Secrets | Externalized | `KEYCLOAK_*` / External Secrets — not in images |
| Resource limits | Memory cap | Compose 768M; K8s deployment limits |
| Container hardening | Mild | `no-new-privileges`; **no** `cap_drop: ALL` (JVM/process needs) |

## Realm policy (hrmanagement)

- Phishing-resistant MFA for privileged roles (IAM-003 follow-on).
- Session idle / max lifespan aligned with agency policy.
- Client secrets rotated via vault; confidential clients only for service accounts.

## Exceptions

See [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md).

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
