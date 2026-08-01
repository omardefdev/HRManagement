# Runbook — Message Queue Backup / Consumer Lag

**Card:** SRE-002  
**Alert:** `RabbitMQQueueDepthCritical` / `MassTransitConsumerLag`  
**Severity:** P1  
**Last reviewed:** 2026-07-31

## Impact

- Asynchronous workflows stall (payroll events, approval notifications, audit log shipping).
- End users see delayed status updates; payroll may miss the disbursement window.
- Messages are durable — no data loss unless disk fills.

## Diagnosis

1. Check RabbitMQ management UI or CLI:
   ```bash
   kubectl exec deploy/rabbitmq -- rabbitmqctl list_queues name messages consumers
   ```
2. Identify queues with depth > threshold (default 10 000).
3. Check consumer pod status:
   ```bash
   kubectl get pods -l app=payroll-consumer
   ```
4. Look for consumer crash loops or OOM kills.
5. Review Grafana dashboard **MassTransit / Consumer Lag**.

## Mitigation

| Step | Action | Expected result |
|------|--------|-----------------|
| 1 | Restart stuck consumers: `kubectl rollout restart deploy/payroll-consumer` | Consumers reconnect |
| 2 | Scale consumers horizontally if queue is draining too slowly | Drain rate increases |
| 3 | If poison message: move to dead-letter queue and skip | Consumer unblocked |
| 4 | If RabbitMQ disk alarm: free disk or expand PVC | Publishes resume |

## Escalation

- If queue not draining after 20 minutes → page **Platform SRE** (SRE-001 roster)
- If payroll window at risk → engage **Incident Commander** (SRE-003)

## Post-resolution

- Verify queue depths return to normal.
- Confirm no messages lost (compare published vs consumed counts).
- Trigger postmortem if payroll window was impacted.
