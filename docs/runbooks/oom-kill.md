# Runbook — Out of Memory (OOM Kill)

**Card:** SRE-002  
**Alert:** `ContainerOOMKilled` / `PodEvicted`  
**Severity:** P1 (Tier-0 service) / P2 (Tier-1/2)  
**Last reviewed:** 2026-07-31

## Impact

- Affected pods restart, causing brief unavailability and in-flight request failures.
- If all replicas OOM simultaneously, the service is fully down.
- Payroll calculation, report generation, and file export are common triggers.

## Diagnosis

1. Identify OOM-killed pods:
   ```bash
   kubectl get pods --field-selector=status.phase=Failed -o wide
   kubectl describe pod <pod-name> | grep -A5 "Last State"
   ```
2. Check memory usage trend in Grafana **K8s / Pod Resources**.
3. Identify the workload causing the spike:
   ```bash
   kubectl top pods -l app=hr-core --sort-by=memory
   ```
4. Review recent deployments or configuration changes.
5. Check for unbounded queries or large file uploads in application logs.

## Mitigation

| Step | Action | Expected result |
|------|--------|-----------------|
| 1 | If crash loop: increase memory limit in deployment manifest | Pod stabilizes |
| 2 | If caused by query: kill the query and add guard (OPS-007) | Memory released |
| 3 | If file upload: enable streaming upload, reject oversized files | Memory bounded |
| 4 | If cluster-wide: scale node pool or evict low-priority workloads | Headroom restored |

## Escalation

- If Tier-0 service OOM > 2 restarts in 10 min → page **Platform SRE**
- If cluster-wide pressure → engage **Incident Commander** (SRE-003)

## Post-resolution

- Tune resource requests/limits based on observed usage.
- Add or adjust HPA if applicable.
- Trigger postmortem if user-facing impact > 1 minute.
