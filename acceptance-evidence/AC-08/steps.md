# AC-08 quarantine reconciliation

- Persona: `platform-admin`
- Correlation ID: `c5141d40-3015-4211-931a-122b15c02a97`
- HTTP observations: `5`

## Observed requests

- `GET /api/tenant-quarantine?resolved=false` -> `200`
- `PATCH /api/employees/a0000001-0000-4000-8000-000000000901` -> `409`
- `POST /api/tenant-quarantine/2f04723c-04e3-47ad-8000-81272b32d41b/resolve` -> `200`
- `GET /api/tenant-quarantine?resolved=false` -> `200`
- `PATCH /api/employees/a0000001-0000-4000-8000-000000000901` -> `200`
