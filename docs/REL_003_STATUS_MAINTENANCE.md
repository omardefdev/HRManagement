# REL-003 — Status communication and maintenance

Not a national public status page (SRE-003). Agency / pilot channel announcements only.

## Maintenance

1. Schedule window; identify services from [service-catalog.json](rel-ops/service-catalog.json).
2. Silence alerts: [`scripts/obs-silence-maintenance.sh`](../scripts/obs-silence-maintenance.sh).
3. Announce before start (template below).
4. Execute change per [REL_003_CHANGE_PROCESS.md](REL_003_CHANGE_PROCESS.md).
5. Verify health; remove silence; announce completion or rollback.

## Status message template

```text
Subject: [STATUS] {SERVICE} — {STARTING|UPDATE|RESOLVED|MAINTENANCE}
Severity: Sev{N}
Impact: {what users see}
Window: {UTC start} – {UTC end or TBD}
Next update: {time or "on resolve"}
Contact: on-call per REL_003_ONCALL_ROSTER
Incident/Change id: {id}
```

## Channels (fill for environment)

| Audience | Channel | Owner |
|---|---|---|
| Pilot agency ops | | program |
| Internal eng | | sre |
| Privacy (if data incident) | | privacy (separate path) |
