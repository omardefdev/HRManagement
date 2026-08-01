# IOP-001 — Semantic Model and National Integration Profiles

## Purpose

Define the canonical semantic model for national integration: identifiers, code lists, date/time/currency conventions, employment/org/payroll semantics, error handling, pagination, and metadata. Profiles map to each national system (Treasury, Civil Registry, Insurance, etc.).

## Scope

- Canonical identifiers and code lists
- Date/time/currency conventions
- Employment, org, and payroll semantics
- Error and pagination contracts
- Classification, purpose, and tenant metadata
- OpenAPI / AsyncAPI alignment
- Schema registry profiles per national system

## Artifacts

| File | Purpose |
|------|---------|
| `docs/iop/canonical-identifiers.json` | Authoritative identifier definitions for person, org, employee, position, payroll |
| `docs/iop/schema-registry-profile.json` | Schema registry entry templates per national integration partner |

## Design Decisions

1. **Owner per field** — every canonical field has a named owner, version, and change policy.
2. **External semantics do not leak** — domain model uses canonical form; adapters translate.
3. **Breaking changes versioned** — schema registry tracks versions; consumers negotiate.
4. **Conformance test** — at least two implementations pass conformance where applicable.

## Acceptance Criteria

- Every field has definition / owner / version
- At least two implementations succeed in conformance
- Breaking changes are versioned
- External semantics do not leak directly into domain model

## Validation

- `scripts/iop001-validate.ps1`
- `Iop001SemanticTests`
