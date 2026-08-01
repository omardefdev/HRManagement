# STR-004 — Config taxonomy (what varies without a fork)

Machine rows: [`str-product/nofork-matrix.json`](str-product/nofork-matrix.json) `surfaces[]`.

| Kind | Meaning | Allowed | Forbidden |
|---|---|---|---|
| data | Tenant-owned records | Scoped DB / files | Separate DB schema fork |
| policy | Rules with effective dates | Versioned packs (PROD-001) | Agency if in rule code |
| template | Letters / forms layouts | Tenant templates + inheritance | Layout repo per agency |
| workflow | Approval graphs | Configurable definitions (PROD-001) | Agency-named code paths |
| branding | Name / colors / logo | Options / branding pack | Forked theme repo |
| integration | Connectors | Config + adapters | Connector fork in mainline |
| code-forbidden | Product source | Never | agency branches / tenant-branch |

## Limits

1. One release binary/digest serves all agencies.
2. Optional modules via STR-003 entitlement, not catalog forks.
3. Surfaces with `prod001Dependency=true` stay Partial until PROD-001 Done.
