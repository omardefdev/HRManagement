# STR-002 — Domain matrix

Source: [`str-legal/control-catalog.json`](str-legal/control-catalog.json) — [ADR-STR-002](ADR-STR-002-legal-control-catalog.md).

## Domains

| Domain | Owner role | Example requirementIds | Typical status |
|---|---|---|---|
| civil_service | legal | CTRL-CS-LAW50-REGIME | Partial / Blocked-Legal (SYD-001) |
| labor | legal | CTRL-LAB-REGIME-MULTI | Partial |
| payroll | payroll | CTRL-PAY-LD-REGISTER | Implemented (via GOV-002) |
| tax | payroll+legal | CTRL-TAX-BRACKETS-INTERIM | Partial (SYD-004 Blocked) |
| pension | legal | CTRL-PEN-SEPARATE-CALC | Partial (SYD-005) |
| privacy | privacy | CTRL-PRV-LAW12-DPIA | Partial (SYD-008) |
| archive | records | CTRL-ARC-RETENTION | Partial (SYD-007) |
| signature_evidence | security | CTRL-SIG-TRUST-CHAIN | Blocked-Legal (SYD-011) |
| accessibility | product | CTRL-A11Y-WCAG22AA | Implemented (UX-002) |
| sovereignty | security | CTRL-SOV-HOSTING | Blocked-Legal (SYD-009) |

## Journeys

| Journey | Evidence |
|---|---|
| DomainCoverage | ≥1 control per domain |
| ControlLinkage | cardIds + evidencePath + testRef |
| ConflictExceptions | exceptions[] + CONFLICT register |
| LegislativeWatch | STR_002_LEGISLATIVE_WATCH.md |
| TraceWalkthrough | STR_002_TRACE_SAMPLES.md (≥3) |
