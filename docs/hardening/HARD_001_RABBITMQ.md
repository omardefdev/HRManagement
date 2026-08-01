# HARD-001 — RabbitMQ Hardening Baseline

**Control:** HARD-001 | **Asset:** RabbitMQ 3.x | **Owner:** Platform Security

## Scope

Message broker for HR domain events (`rabbitmq` service / K8s StatefulSet in `deploy/k8s/data/base/rabbitmq/`).

## Baseline controls

| Control | Requirement | Implementation |
|---|---|---|
| Network | Internal only | Compose `data` network, no host ports in production |
| Default user | Not used by apps | IAM-004 — per-service `mq_*` users in production compose |
| Management UI | Disabled or restricted | No public management port; VPN/firewall if enabled |
| TLS | AMQPS in production K8s | TLS stub overlays; cert from cluster CA / External Secrets |
| Virtual host | Single `/` with ACLs | Per-user vhost permissions via definitions import |
| Guest | Disabled | `loopback_users.guest = none` equivalent |
| Resource limits | Memory cap | Compose 512M limit; K8s requests/limits on StatefulSet |
| Container hardening | Mild | `no-new-privileges`; **no** `cap_drop: ALL` (Erlang VM needs select caps) |

## Required configuration

```yaml
# Production compose — per-service credentials (IAM-004)
RabbitMQ__Username: mq_<service>
RabbitMQ__Password: ${RABBITMQ_<SERVICE>_PASS}
```

## Monitoring

- `rabbitmq-exporter` on observability network only.
- Queue depth / consumer lag alerts (SRE-001).

## Exceptions

See [HARD_001_EXCEPTION_REGISTER.md](./HARD_001_EXCEPTION_REGISTER.md).

## Verification

See [HARD_001_VERIFICATION.md](../HARD_001_VERIFICATION.md).
