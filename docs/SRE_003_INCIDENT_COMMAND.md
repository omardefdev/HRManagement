# SRE-003 — Incident Command, Communication, and Status Page

**Card:** SRE-003  
**Status:** Done (incident command framework + severity model + status page templates + postmortem template)

## Purpose

Define a repeatable, blameless incident management process that ensures rapid response, clear communication, and actionable follow-up for every production incident.

## Severity model

Machine-readable definition: [`sre/incident-severity-model.json`](sre/incident-severity-model.json)

| Severity | Meaning | First comms | Cadence | IC required |
|----------|---------|-------------|---------|-------------|
| Sev-1 | Platform / payroll / auth down; national impact | ≤ 15 min | Every 30 min | Yes |
| Sev-2 | Degraded; single agency severely impacted | ≤ 30 min | Every 60 min | Yes |
| Sev-3 | Limited impact; workaround exists | ≤ 2 hours | Daily | No |
| Sev-4 | Cosmetic / minor; no SLO breach | Best effort | — | No |

## Incident command roles

| Role | Responsibility |
|------|----------------|
| Incident Commander (IC) | Owns timeline, decisions, communications |
| Operations Lead | Executes diagnosis and mitigation |
| Communications Lead | Drafts status updates, agency notifications |
| Scribe | Records timeline, decisions, action items |
| Subject Matter Expert | Domain expertise as needed |

## War room protocol

1. IC opens a dedicated channel (e.g., `#inc-YYYYMMDD-NNN`).
2. All communication in the channel — no side conversations.
3. IC issues structured updates at the defined cadence.
4. IC declares resolution or escalation.
5. Timeline exported and attached to postmortem.

## Status page

Templates: [`sre/status-page-templates.md`](sre/status-page-templates.md)

- Agency-facing and public templates with severity-appropriate language.
- Legal / privacy / SOC escalation triggers documented.
- Regulator notification timelines per GOV-001.

## Postmortem

Template: [`sre/postmortem-template.md`](sre/postmortem-template.md)

- Blameless — focuses on systems and processes, not individuals.
- Must be completed within 5 business days of Sev-1/Sev-2 resolution.
- 100 % of critical action items must have an owner and a due date.
- Reviewed quarterly for pattern analysis and systemic improvements.

## Acceptance criteria

- P1 communication ≤ 15 min then defined cadence
- Quarterly game-day exercise
- Timeline preserved for every Sev-1/Sev-2
- Blameless postmortem within SLA
- 100 % critical actions have owner and date

## Verification

- Major-incident game day
- Communication timing audit
- Action closure audit

## Dependencies

- SRE-001 (Done)
- OBS-002
- GOV-001
