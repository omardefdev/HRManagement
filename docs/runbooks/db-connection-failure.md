# Runbook — Database Connection Failure

**Card:** SRE-002  
**Alert:** `PostgresConnectionPoolExhausted` / `PostgresUnreachable`  
**Severity:** P1  
**Last reviewed:** 2026-07-31

## Impact

- All services relying on the affected PostgreSQL instance fail reads/writes.
- Payroll runs, employee self-service, and leave approvals are blocked.
- Data integrity is **not** at risk (transactions roll back), but availability is.

## Diagnosis

1. Check pod logs for connection errors:
   ```bash
   kubectl logs -l app=hr-core --tail=200 | grep -i "npgsql\|connection\|timeout"
   ```
2. Verify PostgreSQL is reachable:
   ```bash
   kubectl exec deploy/hr-core -- pg_isready -h $PGHOST -p 5432
   ```
3. Check connection pool metrics in Grafana dashboard **HR-Core / Connections**.
4. Check PgBouncer (if applicable) for max-client saturation.
5. Review recent deployments or migrations (`kubectl rollout history`).

## Mitigation

| Step | Action | Expected result |
|------|--------|-----------------|
| 1 | Restart affected pods: `kubectl rollout restart deploy/hr-core` | Fresh connection pool |
| 2 | If pool exhaustion: increase `MaxPoolSize` via ConfigMap and restart | Connections recover |
| 3 | If PostgreSQL unresponsive: failover to standby via Patroni | Read/write restored |
| 4 | If disk full: expand PVC or archive WAL segments | Postgres accepts writes |

## Escalation

- If not resolved in 15 minutes → page **DBA on-call** (SRE-001 roster)
- If data loss suspected → engage **Incident Commander** (SRE-003)

## Post-resolution

- Confirm SLO dashboards return to green.
- Capture timeline in incident channel.
- Trigger postmortem if duration > 5 minutes or data impact detected.
