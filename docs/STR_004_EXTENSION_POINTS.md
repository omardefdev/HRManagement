# STR-004 — Extension points

Allowed variation mechanisms (single product line).

| Extension | Mechanism | Evidence |
|---|---|---|
| Module entitlement | STR-003 Ready modules + agency contract / EnabledModules | STR_003_*, REL_001_FEATURE_FLAGS |
| Branding | `OrganizationBrandingOptions` or future tenant pack | ServiceDefaults |
| Tenant data | TEN-003 filters / RLS | TENANT_* docs |
| Legal decisions | GOV-002 register (payroll) | legal-decisions/register.json |
| Policy / workflow / forms | PROD-001 versioned packs | **Blocked** until PROD-001 |
| Integrations | Configured adapters + EXT conformance | EXT cards |

## Explicitly not extension points

- Source forks under `agency/*`
- Conditional compilation per ministry name
- Copy-paste microservices per tenant
