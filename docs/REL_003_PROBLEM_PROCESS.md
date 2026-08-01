# REL-003 — Problem process (lightweight)

Problems track **root cause** after one or more incidents. Not a full ITIL CMDB.

## Rules

1. Open a problem when: recurring Sev2+, unknown error after Sev1, or three related Sev3 in 30 days.
2. Link `incidentIds[]` and optional `knownErrorId`.
3. Workaround documented before permanent fix when user impact continues.
4. Close when fix verified or accepted risk signed by service owner + SRE.

## Known error (minimal)

| Field | Value |
|---|---|
| knownErrorId | |
| symptom | |
| workaround | |
| permanentFix | |
| status | Open / Mitigated / Closed |

## Handoff

Incident close may set `problemId`. Postmortem actions may create problems.
