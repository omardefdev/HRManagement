# PRD-001 — Product Capability Map

**Card:** PRD-001  
**Version:** 1  
**As of:** 2026-07-31  
**Status:** Production capability model (config/extension, no fork)

## Purpose

Defines how the 14 national HR platform services map to capability domains and how each **agency archetype** classifies module entitlements as `mandatory`, `optional`, `configurable`, or `disabled`. Every capability has an owner role, UAT requirement, and SLO reference.

## Agency Archetypes

| ID | Arabic | English | Scale |
|----|--------|---------|-------|
| `central-ministry` | وزارة مركزية | Central Ministry | XL |
| `governorate` | محافظة | Governorate | Large |
| `autonomous-institution` | مؤسسة مستقلة | Autonomous Institution | Medium |
| `branch-office` | فرع ميداني | Branch Office | Small |
| `specialized-body` | هيئة متخصصة | Specialized Body | Medium |

Source: [`docs/prd-product/agency-archetypes.json`](prd-product/agency-archetypes.json)

## Service → Capability Mapping

| Module | Domain | Owner | UAT | SLO |
|--------|--------|-------|-----|-----|
| hr-core | employee-lifecycle | hr-core-product | Yes | `docs/sre/slo-hr-core.md` |
| hr-contracts | employee-lifecycle | hr-contracts-product | Yes | `docs/sre/slo-hr-contracts.md` |
| hr-health | health-wellness | hr-health-product | Yes | `docs/sre/slo-hr-health.md` |
| hr-movements | workforce-movements | hr-movements-product | Yes | `docs/sre/slo-hr-movements.md` |
| hr-training | learning-talent | hr-training-product | Yes | `docs/sre/slo-hr-training.md` |
| payroll | compensation | payroll-product | Yes | `docs/sre/slo-payroll.md` |
| idcard | identity-credentials | idcard-product | Yes | `docs/sre/slo-idcard.md` |
| records-archive | records-archival | records-product | Yes | `docs/sre/slo-records-archive.md` |
| admin-logistics | admin-operations | admin-logistics-product | Yes | `docs/sre/slo-admin-logistics.md` |
| it-infra | platform-ops | platform-sre | No | `docs/sre/slo-it-infra.md` |
| dev-analytics | analytics-insights | data-analytics | Yes | `docs/sre/slo-dev-analytics.md` |
| compliance | compliance-governance | compliance-product | Yes | `docs/sre/slo-compliance.md` |
| auth-directory | security-identity | iam-platform | Yes | `docs/sre/slo-auth-directory.md` |
| file-storage | document-management | file-platform | Yes | `docs/sre/slo-file-storage.md` |

## Per-Archetype Classification Matrix

| Module | central-ministry | governorate | autonomous-institution | branch-office | specialized-body |
|--------|------------------|-------------|------------------------|---------------|------------------|
| hr-core | mandatory | mandatory | mandatory | mandatory | mandatory |
| hr-contracts | mandatory | mandatory | mandatory | configurable | mandatory |
| hr-health | mandatory | mandatory | configurable | optional | mandatory |
| hr-movements | mandatory | mandatory | mandatory | configurable | mandatory |
| hr-training | mandatory | configurable | optional | optional | mandatory |
| payroll | mandatory | mandatory | mandatory | configurable | mandatory |
| idcard | mandatory | mandatory | configurable | mandatory | configurable |
| records-archive | mandatory | mandatory | mandatory | configurable | mandatory |
| admin-logistics | configurable | optional | optional | optional | disabled |
| it-infra | mandatory | mandatory | mandatory | mandatory | mandatory |
| dev-analytics | configurable | optional | optional | disabled | mandatory |
| compliance | mandatory | mandatory | mandatory | mandatory | mandatory |
| auth-directory | mandatory | mandatory | mandatory | mandatory | mandatory |
| file-storage | mandatory | mandatory | mandatory | mandatory | mandatory |

Source: [`docs/prd-product/capability-map.json`](prd-product/capability-map.json)

## Runtime Enforcement

- **Registry:** `CapabilityRegistry` loads the capability map and archetypes; `IsModuleEnabled` returns `false` for `disabled`.
- **Middleware:** `CapabilityGuardMiddleware` reads `X-Agency-Archetype` header or `CapabilityProfile:Archetype` and blocks disabled modules with HTTP 403.
- **No-fork bridge:** `NoForkCapabilityValidation.ValidateAgainstCapability` aligns STR-004 agency packs with PRD-001 archetype entitlements.

## Personas & UAT

Personas and walkthrough scripts:

- [`docs/prd-product/personas.json`](prd-product/personas.json)
- [`docs/prd-product/uat-walkthrough-template.md`](prd-product/uat-walkthrough-template.md)

## Validation

```powershell
powershell -File scripts/prd001-capability-validate.ps1
dotnet test --filter Prd001CapabilityTests
```
