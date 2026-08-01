# STR-007 — Dashboard tenant / privacy

Policy from `privacyRules` in kpi-matrix:

| Rule | Value |
|---|---|
| tenantScoped | true |
| minCellSize | 5 |
| forbidCrossTenantRollupWithoutWaiver | true |

## Implications

- Value dashboards (even stubs) must filter by tenant; no silent all-tenant rollups.
- Aggregates below min cell size suppress or require waiver.
- Cross-tenant leak fixture (`privacy/cross-tenant-leak.json`) must **FAIL** validate.
- Runtime Dev.Analytics DashboardsController is out of scope; this pack declares the gate for NAT/PROD consumers.
