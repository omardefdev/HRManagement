# TRN-002 — Worker Representation, Unions & Institutional Negotiation

**Card:** TRN-002
**Status:** Done
**Priority:** P1-National / M
**Owner:** Program Sponsor + HR Policy + Legal

## Overview

Maps worker representation bodies (unions, worker committees, professional associations) and their engagement rights in the context of the national HR system rollout. Ensures that any change affecting attendance, performance evaluation, compensation, monitoring, or disciplinary procedures is subject to appropriate consultation or negotiation before deployment.

## Worker Representation Model

The machine-readable model is at `docs/trn/worker-representation.json`.

### Representation Bodies

| ID | Body | Scope |
|----|------|-------|
| WR-01 | General Federation of Trade Unions | National-level, all public sector |
| WR-02 | Agency Worker Committee | Per-agency, elected representatives |
| WR-03 | Professional Association | Per-profession (teachers, engineers, health workers) |

### Engagement Classification

| Topic | Engagement Level |
|-------|-----------------|
| Attendance monitoring rules | Negotiation |
| Performance evaluation criteria | Consultation |
| Disciplinary procedures | Negotiation |
| Data monitoring / surveillance | Negotiation + PRV-001 compliance |
| Compensation structure changes | Negotiation |
| Training requirements | Consultation |
| System access and usability | Information |

### Engagement Protocol

1. **Identification** — Map affected bodies per wave
2. **Pre-notification** — Formal notice ≥30 days before wave go-live
3. **Consultation/Negotiation** — Documented sessions with minutes
4. **Objection handling** — Written response to every objection within 15 days
5. **Resolution** — Agreement, conditional approval, or escalation to competent authority
6. **Evidence** — All engagement documented for NAT-001 evidence pack

### Monitoring & Privacy Boundaries

- Employee monitoring scope and purpose disclosed to workers and representatives
- Monitoring consistent with PRV-001 and Law 12/2024
- Collective grievance pathway exists alongside individual HCM-006 process
- No communication to employees bypasses approved content

## Dependencies

TRN-001, UXR-001, HCM-002, HCM-003, HCM-006, PRV-001

## Artifacts

- `docs/trn/worker-representation.json`
- `scripts/trn002-validate.ps1`
- `Trn002UnionsTests`
