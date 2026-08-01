# SRE-003 — Status Page Communication Templates

**Card:** SRE-003  
**Purpose:** Pre-approved templates for incident communications on the status page and agency notifications.

---

## Template 1 — Investigating (Sev-1 / Sev-2)

> **[Service Name] — Investigating**
>
> We are aware of an issue affecting [brief description]. Our team is actively investigating. We will provide an update within [cadence] minutes.
>
> **Impact:** [Affected services / agencies / journeys]  
> **Started:** [HH:MM UTC]  
> **Next update:** [HH:MM UTC]

---

## Template 2 — Identified

> **[Service Name] — Identified**
>
> We have identified the cause of the issue: [root cause summary]. We are implementing a fix and expect resolution within [ETA].
>
> **Impact:** [Affected services / agencies / journeys]  
> **Started:** [HH:MM UTC]  
> **Next update:** [HH:MM UTC]

---

## Template 3 — Monitoring

> **[Service Name] — Monitoring**
>
> A fix has been applied. We are monitoring to confirm the issue is fully resolved.
>
> **Impact:** [Recap of affected services]  
> **Started:** [HH:MM UTC] | **Fix applied:** [HH:MM UTC]  
> **Next update:** [HH:MM UTC]

---

## Template 4 — Resolved

> **[Service Name] — Resolved**
>
> The issue has been resolved. All services are operating normally.
>
> **Duration:** [HH:MM – HH:MM UTC] ([N] minutes)  
> **Root cause:** [One-line summary]  
> **Postmortem:** Will be published within [N] business days.

---

## Template 5 — Agency notification (Sev-1 / Sev-2)

> **Subject:** [Platform Name] — Service disruption notification
>
> Dear Agency Coordinator,
>
> We are writing to inform you of a service disruption affecting [journeys]. Our team is working to restore service. [Manual fallback procedures per BCP-001 are available at [link]].
>
> We will provide regular updates every [cadence] minutes.
>
> For urgent inquiries, contact the Service Desk: [contact].

---

## Template 6 — Regulator notification (Sev-1 + data/privacy)

> **Subject:** Incident notification — [Platform Name]
>
> [Regulator Name],
>
> In accordance with [regulation reference], we are notifying you of an incident that [may have / has] affected [PII / financial data / service availability].
>
> **Incident ID:** INC-YYYYMMDD-NNN  
> **Detection time:** [HH:MM UTC]  
> **Notification time:** [HH:MM UTC]  
> **Impact assessment:** [Preliminary scope]  
> **Actions taken:** [Summary]  
>
> A full report will follow within [N] hours.

---

## Usage notes

- IC or Communications Lead selects the appropriate template.
- All timestamps in UTC.
- Legal review required before regulator notifications.
- Archive all published communications in the incident timeline.
