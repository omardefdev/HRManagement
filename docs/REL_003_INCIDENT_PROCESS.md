# REL-003 — Incident process

Severity/SLA: [REL_003_SEVERITY_SLA.md](REL_003_SEVERITY_SLA.md). On-call: [REL_003_ONCALL_ROSTER.md](REL_003_ONCALL_ROSTER.md). Alert runbooks: [OBS_ALERT_RUNBOOKS.md](OBS_ALERT_RUNBOOKS.md).

## Flow

1. **Detect** — alert, user report, or probe failure.
2. **Triage** — assign severity; ack within MTTA; page correct onCallTier from catalog.
3. **Command** — Sev1: open bridge; site-loss uses [OPS_DR_COMMUNICATION_TREE.md](OPS_DR_COMMUNICATION_TREE.md). Sev2+: owner leads with SRE backup.
4. **Mitigate / resolve** — follow service `runbookPath`; restore user impact.
5. **Communicate** — status updates per [REL_003_STATUS_MAINTENANCE.md](REL_003_STATUS_MAINTENANCE.md).
6. **Close** — set `resolvedAt`; link problem if recurring; trigger postmortem when required by severity rules.

## Record (minimum fields)

| Field | Required |
|---|---|
| incidentId | Yes |
| severity | Yes |
| serviceId | Yes (catalog id) |
| detectedAt / acknowledgedAt / resolvedAt | Yes |
| commander / ackBy | Yes |
| summary / timeline | Yes |
| postmortemRequired | Yes |

## Game day

Synthetic measurement via `scripts/rel003-incident-game-day.ps1` and `testdata/rel003/game-day/`.
