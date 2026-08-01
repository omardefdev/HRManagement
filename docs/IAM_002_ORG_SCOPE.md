# IAM-002 — Fail-Closed Org Scope

## Objective

Ensure that a missing, malformed, or out-of-tenant `scope_org_unit_id` claim always produces a **403 Forbidden** — never unrestricted access. The `OrgAccessScope` type uses explicit states (`Unrestricted`, `Restricted`, `Denied`) so that `null` is never treated as all-access.

## Design

### OrgAccessScope (SharedKernel / ServiceDefaults)

| State | Meaning |
|---|---|
| `Unrestricted` | Admin / HR-Core / non-branch role — no org filter. |
| `Restricted(ids)` | Branch manager with valid claim — access limited to subtree. |
| `Denied` | Branch manager with missing or invalid claim — **deny all**. |

`null` is never a valid scope value. Every code path that resolves scope produces one of the three explicit states.

### OrgScopeAuthorizationHandler

`OrgScopeAuthorizationHandler` implements `AuthorizationHandler<OrgScopeRequirement>`:

1. Unauthenticated → requirement not satisfied (default ASP.NET behavior).
2. Not branch-scoped (Admin, HR-Core, etc.) → `Succeed`.
3. Branch-scoped with valid `scope_org_unit_id` → `Succeed`.
4. Branch-scoped with missing/invalid claim → `Fail` with explicit reason.

### Controller enforcement

`OrgScopeControllerExtensions` provides:
- `ForbidIfOrgScopeDenied(scope)` — returns 403 if `Denied`.
- `ForbidIfOrgUnitOutOfScope(scope, orgUnitId)` — returns 403 if org unit is outside allowed set.
- `WhereInOrgScope(query, selector)` — filters IQueryable to allowed org units; Denied produces empty set.

### OrgScopeResolver

Resolves branch scope via `OrgUnitSnapshot.MaterializedPath` prefix (PERF-003). Caches results with 90-second TTL.

## Acceptance criteria mapping

| Criterion | Implementation |
|---|---|
| Missing/invalid/out-of-tenant claim → 403, not full access | `OrgScopeAuthorizationHandler` fails; `OrgAccessScope.Denied()` in resolver |
| Branch manager cannot access sibling org unit via direct GUID | `OrgScopeResolver` restricts to subtree; `WhereInOrgScope` filters queries |
| Sensitive endpoints in route/action/resource policy matrix | Module policies + `OrgScopeRequirement` layered per endpoint |
| Negative tests ≥ positive tests per sensitive resource | `Iam002OrgScopeTests` covers fail-closed, sibling denial, admin pass |

## Validation

```powershell
scripts/iam002-validate.ps1
```

## Related cards

- IAM-001 (JWT / Keycloak / TLS)
- TEN-001 / TEN-002 (tenant boundary — enforced separately via `ITenantContext`)
- PERF-003 (materialized path performance)
