# SRE-003 — Postmortem Template

**Card:** SRE-003  
**Principle:** Blameless — focus on systems and processes, not individuals.

---

## Incident metadata

| Field | Value |
|-------|-------|
| Incident ID | INC-YYYYMMDD-NNN |
| Severity | Sev-1 / Sev-2 |
| Date | YYYY-MM-DD |
| Duration | HH:MM – HH:MM UTC (N minutes) |
| Incident Commander | Name |
| Author | Name |
| Reviewers | Names |
| Status | Draft / Reviewed / Published |

---

## Summary

_One paragraph describing what happened, the user impact, and how it was resolved._

---

## Timeline

| Time (UTC) | Event |
|------------|-------|
| HH:MM | Alert fired: `[alert name]` |
| HH:MM | On-call acknowledged |
| HH:MM | IC engaged, war room opened |
| HH:MM | Root cause identified |
| HH:MM | Fix deployed |
| HH:MM | Service restored, monitoring |
| HH:MM | Incident resolved |

---

## Impact

| Metric | Value |
|--------|-------|
| Users affected | N |
| Agencies affected | N |
| Requests failed | N |
| Data loss / corruption | Yes / No |
| SLO breach | Yes / No |
| Financial impact | Description or N/A |

---

## Root cause

_Detailed technical explanation of the root cause. What failed, and why._

---

## Contributing factors

1. _Factor 1_
2. _Factor 2_

---

## Detection

_How was the incident detected? Alert, user report, monitoring?_  
_Was detection timely? If not, what would improve it?_

---

## Response

_What went well in the response? What could be improved?_

---

## Action items

| ID | Action | Owner | Priority | Due date | Status |
|----|--------|-------|----------|----------|--------|
| AI-001 | _Description_ | _Name_ | P1/P2/P3 | YYYY-MM-DD | Open |
| AI-002 | _Description_ | _Name_ | P1/P2/P3 | YYYY-MM-DD | Open |

---

## Lessons learned

### What went well

1. _Item_

### What could be improved

1. _Item_

### Where we got lucky

1. _Item_

---

## References

- Runbook used: [link]
- Related incidents: [links]
- Monitoring dashboards: [links]
