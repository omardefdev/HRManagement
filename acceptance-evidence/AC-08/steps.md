# AC-08 quarantine reconciliation

- Persona: `platform-admin`
- Correlation ID: `2e0cd1b2-971f-4a2a-90bc-cdb6413af6e6`
- HTTP observations: `5`

## Observed requests

- `GET /api/tenant-quarantine?resolved=false` -> `200`
- `PATCH /api/employees/a0000001-0000-4000-8000-000000000901` -> `409`
- `POST /api/tenant-quarantine/77f63f3b-f8ed-4f09-91eb-21ef84303361/resolve` -> `200`
- `GET /api/tenant-quarantine?resolved=false` -> `200`
- `PATCH /api/employees/a0000001-0000-4000-8000-000000000901` -> `200`
