# TRN-001 — Training, Change Management & Organizational Readiness

**Card:** TRN-001
**Status:** Done
**Priority:** P1-National / L
**Owner:** Change Management Office + Training + HR Policy

## Overview

Establishes the training and change management framework for national HR system rollout. Covers persona-based curricula design, training delivery across 14 governorates, competency verification, resistance management, and readiness assessment before each deployment wave.

## Persona-Curricula Model

The machine-readable model is at `docs/trn/persona-curricula.json`.

### Training Personas

| ID | Persona | Description |
|----|---------|-------------|
| TP-01 | HR Officer | Day-to-day HR operations: recruitment, attendance, cases |
| TP-02 | Payroll Specialist | Payroll processing, arrears, reconciliation |
| TP-03 | Agency Admin | Configuration, user management, reporting |
| TP-04 | Manager (MSS) | Team management, approvals, performance |
| TP-05 | Employee (ESS) | Self-service: leave, payslips, profile |
| TP-06 | Auditor | Evidence review, compliance monitoring |
| TP-07 | IT Support | Technical support, incident triage |

### Curriculum Structure

Each persona has a curriculum composed of:

1. **Foundation** — System overview, navigation, Arabic/English interface
2. **Role-specific** — Workflows, screens, and business rules for the persona
3. **Compliance** — Data protection, security awareness, fraud indicators
4. **Assessment** — Practical exam with minimum pass score
5. **Certification** — Verifiable completion certificate (DOC-001)

### Readiness Assessment

| Criterion | Threshold |
|-----------|-----------|
| Training completion rate | ≥90% of designated users per wave |
| Assessment pass rate | ≥80% pass on first attempt |
| Environment readiness | UAT environment available with production-like data |
| Help desk staffed | Tier-1 support available in Arabic |
| Communication plan executed | All affected staff notified ≥14 days before go-live |

## Change Management

- Resistance register maintained per agency with documented mitigations
- Stakeholder analysis per wave with engagement plan
- Communication materials approved before distribution (TRN-002)
- Feedback loop from training sessions incorporated into product backlog
- Adoption metrics tracked in STR-007

## Dependencies

UXR-001, PRD-001, COM-001, STR-007, HCM-005

## Artifacts

- `docs/trn/persona-curricula.json`
- `scripts/trn001-validate.ps1`
- `Trn001TrainingTests`
