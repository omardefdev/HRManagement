# IAM-002 — Fail-Closed Org Scope

## Objective

Ensure that a missing, malformed, or out-of-tenant `scope_org_unit_id` claim always produces a **403 Forbidden** — never unrestricted access. The `OrgAccessScope` type uses explicit states (`Unrestricted`, `Restricted`, `Denied`) so that `null` is never treated as all-access.

Since PLAN-52 (IAM-D), the same scope also keeps a restricted user inside his branch on every screen he reaches: one registry of scope keys per service and one action filter, with a stance declared on every controller.

## Design

### OrgAccessScope (SharedKernel / ServiceDefaults)

| State | Meaning |
|---|---|
| `Unrestricted` | Admin / HR-Core / non-branch role — no org filter. |
| `Restricted(ids)` | Branch manager with valid claim — access limited to subtree. |
| `Denied` | Branch manager with missing or invalid claim — **deny all**. |

`null` is never a valid scope value. Every code path that resolves scope produces one of the three explicit states. The scope applies only with the `branch-manager` role (D-IAM-3); the filter and the registry read `OrgAccessScope`, never a role name.

### The mechanism (PLAN-52, `src/Shared/HRManagement.ServiceDefaults/OrgScope/`)

- **The stance of each controller** (`OrgScopeAttributes.cs`), on the class, overridable on an action:
  - `[OrgScoped(typeof(T))]` — the records are scoped through entity `T`; `OutOfScope = OrgScopeAnswer.Forbid` keeps an older 403 where a test pins it (AC-06's lookup, AC-32's assets, PLAN-34's twelve lists, QA-034's `HireWizard.Index`/`Continue`), else 404;
  - `[OrgScopeCentralOnly(reason)]` — 403 to a restricted user (catalogue writes, D-52-3; types with no unit in the service);
  - `[OrgScopeNotApplicable(reason)]` — reference data, self-service by ownership, service-to-service, a page with no org record;
  - HR.Core's `[RecruitmentOrgScope]` implements the same marker.
- **The registry** (`OrgScopeKeyRegistry`, one per service: `HrCoreOrgScopeKeys`, `ContractsOrgScopeKeys`, `AdminLogisticsOrgScopeKeys`, `MovementsOrgScopeKeys`) gives each entity type an expression to its unit or units, translated to SQL on PostgreSQL and SQLite:
  - a direct unit (`OrgUnitId`), through an employee (`Employee.PrimaryOrgUnitId` in HR.Core, the local `EmployeeOrgUnitSnapshot` elsewhere), through a parent (`SuccessionPlan` → `KeyPosition`, `ReceiptRecord` → `SupplyOrder`, `MaintenanceRecord` → `Asset`'s custodian), or several units that must all be in scope (a transfer's two sides);
  - a null unit, or a missing snapshot, is no key: the record is central (D-52-1), so 404 for a restricted user;
  - id names map to entity types per controller (`id`, `employeeId`, `rosterId`, `transactionId`…); `[OrgScopeId(typeof(T))]` changes one, `[NotOrgScopeKey(reason)]` declares an id that names no scoped record; a posted `OrgUnitId` is a key (`OrgScopeUnit`).
- **The filter** (`OrgScopeActionFilter<TContext>`, order −3000, before `[ApiController]`'s automatic 400) acts for a restricted user only; an unrestricted user costs no query:
  - `Denied`, or a central-only action → 403;
  - every keyed id of the action (route, query, form, a bound model's properties, each element of a list) is resolved before the action runs: out of scope, or no key → the stance's answer (404, or the kept 403); a parent and a child id are both checked, and the action still checks that the child belongs to the parent.
- **Lists, exports and pick-lists** are not the filter's: in an `[OrgScoped]` controller each `Index`, export (direct, and the query an export job runs later: the job records the requester's scope) and pick-list goes through `InOrgScopeAsync` / `WhereInOrgScope(registry, scope, db)`.
- **New records:** a restricted user's record without a unit takes his scope's root (`GetRestrictedRootAsync`, D-52-2); one in another branch's unit is refused by the filter.
- `OrgScopeRequirement`, its handler and `AddOrgScopeAuthorization()` were removed by PLAN-52.

### Controller helpers

`OrgScopeControllerExtensions` provides:
- `ForbidIfOrgScopeDenied(scope)` — returns 403 if `Denied`.
- `ForbidIfOrgUnitOutOfScope(scope, orgUnitId)` — returns 403 if org unit is outside allowed set.
- `WhereInOrgScope(query, selector)` and `WhereInOrgScope(query, registry, scope, db)` — filter an `IQueryable` to the allowed org units; Denied produces an empty set.

### OrgScopeResolver

Resolves branch scope via `OrgUnitSnapshot.MaterializedPath` prefix (PERF-003). Caches results with 90-second TTL.

### Transfers (PLAN-52, D-52-4)

A transferred user who carries a scope loses it (fail-closed): Auth.Directory clears the local scope and audits it, and with `Keycloak:DirectorySync:Enabled` removes the token attribute (a failure retries the message); with the sync off a warning and `directory_scope_sync_skipped_total` say the token keeps it until the sync runs. HR.Health's employee snapshot follows the transfer, as HR.Contracts' and Admin.Logistics' do.

## Acceptance criteria mapping

| Criterion | Implementation |
|---|---|
| Missing/invalid/out-of-tenant claim → 403, not full access | `OrgAccessScope.Denied()` in the resolver; the filter answers 403 |
| Branch manager cannot access sibling org unit via direct GUID | The filter resolves every id through the registry (404, or the kept 403) |
| Lists, exports and pick-lists show no other branch's record | `InOrgScopeAsync` / `WhereInOrgScope` in each `[OrgScoped]` controller |
| Every controller declares its stance | `OrgScopeCoverageTests` (BOLA v2, reflection over the fifteen web assemblies); latent controllers are `IAM-H` rows of `tests/architecture/org-scope-allowlist/` |
| Negative tests ≥ positive tests per sensitive resource | `Iam002OrgScopeTests`, `Iam052*` (unit, integration generated from the registries, Testcontainers), AC-IAM-52a..e |

## Validation

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam002-validate.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/iam052-validate.ps1 -SelfTest
```

## Related cards

- IAM-001 (JWT / Keycloak / TLS)
- IAM-D / PLAN-52 (branch scope on every screen, BOLA v2)
- TEN-001 / TEN-002 (tenant boundary — enforced separately via `ITenantContext`)
- PERF-003 (materialized path performance)
