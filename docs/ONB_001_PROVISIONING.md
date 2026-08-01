# ONB-001 — Automated Agency Provisioning from Signed Setup Pack

## Purpose

Enable fully automated agency provisioning from a signed manifest — covering tenant, identity, roles, segregation of duties, policies, workflows, reference data subset, language, calendar, branding, integrations, retention, monitoring, backup, and support — with validation, four-eyes publish, and rollback.

## Scope

- Signed agency manifest schema with required sections
- Tenant creation with archetype and governorate
- Identity realm bootstrap (no inline secrets)
- Role catalog and SoD matrix binding
- Policy and workflow pack promotion
- Reference data subset from MDM-001 catalogs
- Arabic RTL-first language defaults
- Asia/Damascus calendar and holiday policy binding
- Integration connector profiles
- Retention matrix binding (SYR-015)
- Observability and backup restore tests
- Support tier and on-call binding

## Manifest Schema

See `docs/onb/agency-manifest-schema.json` for the JSON Schema and example manifest.

### Required Sections

| Section | Purpose |
|---------|---------|
| tenant | Agency display name, archetype, governorate |
| identity | Realm and sealed bootstrap reference |
| roles | Role catalog + SoD matrix |
| policies | Policy pack IDs; national mandatory preserved |
| workflows | Workflow pack IDs |
| referenceSubset | MDM catalog kinds to seed |
| language | ar-SY primary, RTL-first |
| calendar | Asia/Damascus + holiday policy ref |
| integrations | Connector profile IDs |
| retention | SYR-015 matrix reference |
| monitoring | Observability profile |
| backup | Schedule + restore test required |
| support | Service desk tier + on-call |

## Provisioning Rules

1. **No secret in pack** — bootstrap refs point to vault, not inline values
2. **No manual DB edits** — all provisioning via manifest pipeline
3. **National mandatory controls preserved** — `preserveNationalMandatory: true`
4. **Automated observability/backup/access tests** — post-provision validation
5. **Rollback supported** — `rollbackRef` on every manifest
6. **Four-eyes publish** — minimum 2 reviewer roles

## Acceptance Criteria Mapping

- [x] Agency from zero automatically — manifest schema + example
- [x] No secret in pack — `provisioningRules.noSecretInPack`
- [x] National mandatory controls preserved — policies rule
- [x] Observability/backup/access tests automated — provisioning rules
- [x] Rollback supported — rollbackRef required
- [x] No manual DB edits — provisioning rules

## Artifacts

| File | Purpose |
|------|---------|
| `docs/ONB_001_PROVISIONING.md` | This document |
| `docs/onb/agency-manifest-schema.json` | Manifest JSON Schema |
| `scripts/onb001-validate.ps1` | Validates ONB-001 artifacts |
| `Onb001ProvisioningTests.cs` | Unit tests |

## Validation

- `scripts/onb001-validate.ps1`
- `Onb001ProvisioningTests`
