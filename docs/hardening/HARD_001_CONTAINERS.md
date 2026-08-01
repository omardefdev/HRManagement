# HARD-001 — Container Runtime Hardening Baseline

**Control:** HARD-001 | **Asset:** All containerized workloads | **Owner:** Platform Security

## Scope

Docker Compose production stack and Kubernetes Deployments/StatefulSets for HR Management.

## Baseline controls (application services)

Applies to: `hr-gateway`, `*-web` ASP.NET services.

| Control | Requirement | Compose | Kubernetes |
|---|---|---|---|
| Non-root | Process UID ≠ 0 | `USER $APP_UID` in Dockerfiles | `runAsNonRoot: true`, `runAsUser: 1654` |
| No new privileges | Block privilege escalation | `security_opt: [no-new-privileges:true]` | `allowPrivilegeEscalation: false` |
| Cap drop | Drop all unless required | `cap_drop: [ALL]` | `capabilities.drop: [ALL]` |
| Read-only rootfs | Where no runtime writes | Optional — see exceptions | `readOnlyRootFilesystem: true` + emptyDir mounts |
| Resource limits | CPU + memory caps | `deploy.resources.limits` | `resources.limits` |
| Health checks | Liveness/readiness | `app-healthcheck` anchor | probes in Deployment manifests |
| Minimal image | Distroless/runtime-only final stage | `aspnet:10.0` runtime stage | Same image in K8s |

## Writable volume exceptions

Services with integration outbox volumes **cannot** use read-only rootfs without dedicated mount points:

| Service | Writable path | Reason |
|---|---|---|
| `hr-core-web` | `/app/integration-outbox` | Outbox pattern |
| `payroll-web` | `/app/integration-outbox` | Outbox pattern |

## Infrastructure services (milder hardening)

PostgreSQL, RabbitMQ, Keycloak, MinIO, observability stack:

- Apply `no-new-privileges:true`.
- **Do not** apply `cap_drop: ALL` — breaks initialization (chown, Erlang, JVM).
- Document any additional caps in exception register if unavoidable.

## Compose anchors

Production overrides define:

- `x-app-hardening` — full hardening for ASP.NET apps.
- `x-infra-hardening-mild` — `no-new-privileges` only for data plane.

## Kubernetes reference

See `deploy/k8s/base/pod-security/security-context-defaults.yaml` and PSA namespace labels.

## Exceptions

See [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md).

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
