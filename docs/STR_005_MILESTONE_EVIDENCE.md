# STR-005 — Milestone evidence

Evidence required at program milestones. Soft-links REL-002 pilot Go/No-Go and national/Syria checklists (file 14); does not replace them.

| Milestone | Minimum evidence | Boards / seats |
|---|---|---|
| Pilot Go/No-Go | REL-002 pack + decision-log entry (GoLive or ScheduleDelay) | Program + Security |
| Wave start | Exception register current (no expired open exceptions) + risk review | Program |
| National / production Go-Live | Multi-seat signatures per GO_LIVE_GATE; ADR for GoLive type; SYD-017 names when available | Sponsor + Security + Architecture (+ Data if data impact) |
| Post-incident return-to-green | Incident decision + exception expiry or close + Change review | Security + Change |
| Law / gazette change | LawChange decision + ADR + GOV-002 handoff when payroll rule | Legal + Architecture |

## Artifact pointers

- Decision log: `docs/str-governance/decision-log.json`
- Risk register: `docs/str-governance/risk-register.json`
- Simulations: `testdata/str005/scenarios/`
- Go-Live fixtures: `testdata/str005/go-live/`
