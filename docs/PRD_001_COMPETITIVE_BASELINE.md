# PRD-001 — Competitive Baseline Framework

**Card:** PRD-001  
**Version:** 1  
**As of:** 2026-07-31

## Purpose

Provides a capability-by-capability comparison framework against competing government HR / ERP systems. Used during agency sign-off and national product council reviews—not as marketing claims, but as structured gap analysis input.

## Comparison Dimensions

For each capability domain, score each system on a 4-point scale:

| Score | Label | Definition |
|-------|-------|------------|
| 0 | Absent | No supported workflow or requires custom fork |
| 1 | Partial | Manual workaround or bolt-on integration |
| 2 | Configurable | Supported via configuration or extension point |
| 3 | Native | First-class, versioned, UAT-backed capability |

## Baseline Competitors (placeholder slots)

| Slot | System class | Notes |
|------|--------------|-------|
| A | Legacy on-prem ministry HR | Typical COBOL/SQL monolith |
| B | Regional ERP HR module | SAP/Oracle/Odoo public sector |
| C | National HCM SaaS | Cloud multi-tenant competitor |
| D | **HRManagement (this platform)** | PRD-001 capability map |

## Capability Comparison Matrix

| Capability Domain | Key Modules | A | B | C | D (HRManagement) | Gap / Differentiator |
|-------------------|-------------|---|---|---|------------------|----------------------|
| Employee lifecycle | hr-core, hr-contracts | | | | 3 | Archetype entitlements without fork |
| Compensation | payroll | | | | 3 | Immutable payroll config + golden runs |
| Workforce movements | hr-movements | | | | 3 | Cross-agency transfer with history |
| Health & wellness | hr-health | | | | 2 | Medical employment effect rules |
| Learning & talent | hr-training | | | | 2 | Specialized-body mandatory path |
| Identity & credentials | idcard, auth-directory | | | | 3 | National directory integration |
| Records & archival | records-archive, file-storage | | | | 3 | Evidence chain + retention |
| Admin & logistics | admin-logistics | | | | 1 | Optional/disabled by archetype |
| Platform operations | it-infra | | | | 3 | ServiceDefaults + observability |
| Analytics | dev-analytics | | | | 2 | Disabled for branch-office by design |
| Compliance | compliance | | | | 3 | STR-002 control catalog binding |

## Evaluation Workflow

1. **Select archetype** — e.g. `governorate` or `specialized-body`.
2. **Filter mandatory capabilities** — from `agency-archetypes.json` entitlements.
3. **Run UAT walkthrough** — per [`uat-walkthrough-template.md`](prd-product/uat-walkthrough-template.md).
4. **Score competitors** — product council fills A/B/C columns; D is derived from capability map.
5. **Record gaps** — any score where D &lt; 2 for a mandatory capability blocks archetype sign-off.
6. **Sign-off** — agency representatives attest config/extension suffices (no fork).

## Evidence Artifacts

| Artifact | Path |
|----------|------|
| Archetype entitlements | `docs/prd-product/agency-archetypes.json` |
| Capability map | `docs/prd-product/capability-map.json` |
| Personas | `docs/prd-product/personas.json` |
| UAT scripts | `docs/prd-product/uat-walkthrough-template.md` |
| Validation script | `scripts/prd001-capability-validate.ps1` |
| Unit tests | `src/Tests/HRManagement.UnitTests/Prd001CapabilityTests.cs` |

## Review Cadence

- **Quarterly:** refresh competitor scores for changed modules.
- **Per release digest:** re-run validation script and UAT mandatory paths.
- **Pre go-live:** witnessed walkthrough for each onboarded archetype.
