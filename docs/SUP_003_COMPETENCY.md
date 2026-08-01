# SUP-003 — Competency, Knowledge Transfer & Team Continuity

**Card:** SUP-003  
**Status:** Done  
**Priority:** P1-National / L  
**Owner:** Workforce + Service Owners

## Overview

Defines the competency matrix for the HRManagement platform team, ensuring two-deep
coverage for every Tier-0 skill. No critical system knowledge may reside with a single
person. Onboarding/offboarding knowledge transfer is formalized and measurable.

## Competency Matrix

See `docs/sup/competency-matrix.json` for the structured Tier-0 skill inventory with
primary and secondary owners.

### Tier-0 Skills (Critical — require two-deep coverage)

| Skill | Domain | Primary | Secondary | Certification |
|-------|--------|---------|-----------|---------------|
| PostgreSQL DBA | Database | DBA Lead | Platform Engineer | Optional PGCA |
| Keycloak IAM | Identity | IAM Lead | Security Engineer | — |
| Kubernetes Operations | Infrastructure | DevOps Lead | SRE-1 | CKA recommended |
| .NET Core / EF Core | Application | Tech Lead | Senior Dev | — |
| RabbitMQ / MassTransit | Messaging | Platform Engineer | Backend Dev | — |
| Incident Response (SOC) | Security | SOC Lead | SRE-2 | — |
| Payroll Domain | Business | Payroll SME | HR Officer | — |
| Arabic Localization / RTL | Localization | UI Lead | Frontend Dev | — |
| Network / Firewall | Security | Network Engineer | DevOps Lead | — |
| Backup / DR | Operations | SRE Lead | DBA Lead | — |

### Coverage Rules

1. Every Tier-0 skill must have at least 2 qualified individuals.
2. Primary and secondary must not be in the same reporting chain (bus-factor resilience).
3. Shadow/rotation sessions scheduled quarterly.
4. Knowledge freshness validated annually via practical exam or drill.

## Knowledge Transfer Protocol

### Onboarding

1. Assign mentor from Tier-0 skill area
2. Complete shadow rotation (minimum 2 weeks per domain)
3. Pass practical assessment (hands-on lab)
4. Document new knowledge in runbook contributions

### Offboarding

1. Knowledge dump sessions (recorded)
2. Runbook review and update
3. Credential rotation
4. Successor sign-off

## Training & Labs

- Internal lab environment mirrors production topology
- Monthly incident simulation exercises
- Quarterly cross-training rotations
- Annual certification review

## Supplier Knowledge Transfer

When vendor support contracts change or expire:
1. Full documentation handover
2. Recorded knowledge transfer sessions
3. Lab validation of independent operations
4. 30-day parallel support period

## Verification

- Practical exams for DBA, IAM, K8s, incident response
- Bus-factor absence exercise (key person unavailable for 48h)
- Knowledge freshness audit (quarterly)
- Coverage matrix reviewed at each sprint retrospective

## Files

- `docs/sup/competency-matrix.json` — structured competency inventory
- `scripts/sup003-validate.ps1` — artifact validation
- `Sup003CompetencyTests.cs` — xUnit tests
