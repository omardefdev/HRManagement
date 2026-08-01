# MIG-002 — Canonical model (migration-facing)

Machine source: `canonicalEntities[]` in [`mig-canonical/code-map-matrix.json`](mig-canonical/code-map-matrix.json).

Joins platform contracts (GOV-003 / master-*.v1) without replacing runtime entities.

| entity | fields (sample) | provenance | effective dates |
|---|---|---|---|
| employee | NationalId, FullName, CurrentStatus, PrimaryOrgUnitId, Gender, Cadre | Yes | Yes |
| org | Code, NameAr, MaterializedPath | Yes | Yes |
| job | PositionTitle | Yes | No (MDM-001 later) |
| payroll | BaseSalary, RegimeCode | Yes | Yes |
| document | DocumentType, LegacyDocId | Yes | No |

Identifiers: platform `Guid` keys; legacy EmpNo/NAT_ID stored as **references** only (see LEGACY_ID_POLICY). TenantId from TEN-001 — never from EmpNo.
