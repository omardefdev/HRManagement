# REL-003 — Severity and SLA (MTTA / MTTR)

Unified severity for ops incidents. Maps to OBS alert labels where useful (`page` ≈ Sev1/Sev2).

| Severity | Definition | MTTA target | MTTR target | Example |
|---|---|---|---|---|
| Sev1 | Site or payroll-cutoff stop; data-loss risk | 15 min | 4 h | Multi-service outage; DR site loss (also OPS-004 IC) |
| Sev2 | Major journey degraded; SLO burn fast | 30 min | 8 h | Gateway availability burn; payroll fail ratio high |
| Sev3 | Partial degradation; workaround exists | 4 h | 3 d | Single non-critical service; elevated latency |
| Sev4 | Cosmetic / request / advisory | 1 business day | 10 d | Docs, non-prod noise |

## Binding rules

1. Sev1 always opens incident command; site-loss uses [OPS_DR_COMMUNICATION_TREE.md](OPS_DR_COMMUNICATION_TREE.md).
2. MTTA = time from `detectedAt` to `acknowledgedAt` (on-call ack).
3. MTTR = time from `detectedAt` to `resolvedAt` (user-impact cleared).
4. Exceeding target without signed waiver fails game-day harness / Conditional Go on readiness.
5. Postmortem required for Sev1 and any Sev2 with customer-visible impact > 1 h.

## OBS mapping

| OBS severity label | Default REL-003 severity |
|---|---|
| page | Sev1 or Sev2 (owner decides from blast radius) |
| ticket | Sev3 |
| warning / info | Sev4 |
